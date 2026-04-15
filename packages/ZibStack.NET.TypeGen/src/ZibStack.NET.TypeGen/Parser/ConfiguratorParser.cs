using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.TypeGen.Generator;

/// <summary>
/// Reads the user's <c>ITypeGenConfigurator</c> implementation by walking the
/// syntax tree of its <c>Configure(ITypeGenBuilder b)</c> method — no runtime
/// invocation. The fluent DSL (<c>b.TypeScript(...)</c>, <c>b.ForType&lt;T&gt;()...</c>)
/// is reconstructed from the call chain; anything the parser can't statically
/// resolve surfaces as a diagnostic (<c>TG0012</c> unknown call, <c>TG0013</c>
/// non-literal argument).
/// </summary>
internal static class ConfiguratorParser
{
    private const string ConfiguratorInterface = "ZibStack.NET.TypeGen.ITypeGenConfigurator";

    public sealed class PerTypeOverrides
    {
        public string? TsName { get; set; }
        public string? OpenApiName { get; set; }
        public string? OutputDir { get; set; }
        public bool Ignore { get; set; }
        public bool TsIgnore { get; set; }
        public bool OpenApiIgnore { get; set; }

        /// <summary>Per-property fluent overrides keyed by property name (case-sensitive, matches C# source).</summary>
        public Dictionary<string, PerPropertyOverrides> Properties { get; } = new();
    }

    public sealed class PerPropertyOverrides
    {
        public string? TsName { get; set; }
        public string? TsType { get; set; }
        public string? OpenApiName { get; set; }
        public string? OpenApiType { get; set; }
        public string? OpenApiRef { get; set; }
        public string? OpenApiFormat { get; set; }
        public string? OpenApiDescription { get; set; }
        public bool? OpenApiNullable { get; set; }
        public bool Ignore { get; set; }
        public bool TsIgnore { get; set; }
        public bool OpenApiIgnore { get; set; }
    }

    public sealed class Parsed
    {
        public GlobalSettings Settings { get; } = new();
        public Dictionary<string, PerTypeOverrides> PerType { get; } = new();
    }

    /// <summary>
    /// Returns the parsed configuration, or <c>null</c> if the project has no
    /// <c>ITypeGenConfigurator</c> implementation. Diagnostics (duplicate configurators,
    /// unknown fluent calls, non-literal args) are reported via <paramref name="report"/>.
    /// </summary>
    public static Parsed? Parse(Compilation compilation, System.Action<Diagnostic> report)
    {
        var iface = compilation.GetTypeByMetadataName(ConfiguratorInterface);
        if (iface is null) return null;

        // Find every class/struct in source that implements ITypeGenConfigurator.
        var impls = new List<INamedTypeSymbol>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var sym = model.GetDeclaredSymbol(decl) as INamedTypeSymbol;
                if (sym is null) continue;
                if (sym.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iface)))
                    impls.Add(sym);
            }
        }

        if (impls.Count == 0) return null;

        if (impls.Count > 1)
        {
            report(Diagnostic.Create(
                TypeGenDiagnostics.MultipleConfigurators,
                impls[0].Locations.FirstOrDefault() ?? Location.None,
                impls.Count,
                string.Join(", ", impls.Select(s => s.ToDisplayString()))));
            // Still parse the first — avoids leaving the project with defaults on a typo.
        }

        var configurator = impls[0];
        var configureMethod = configurator.GetMembers("Configure").OfType<IMethodSymbol>().FirstOrDefault();
        if (configureMethod is null) return null;

        var methodSyntax = configureMethod.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (methodSyntax?.Body is null && methodSyntax?.ExpressionBody is null) return null;

        var semantic = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
        var parsed = new Parsed();

        var statements = methodSyntax.Body?.Statements
            ?? (IEnumerable<StatementSyntax>)new[] { SyntaxFactory.ExpressionStatement(methodSyntax.ExpressionBody!.Expression) };

        foreach (var stmt in statements)
        {
            if (stmt is not ExpressionStatementSyntax exprStmt) continue;
            if (exprStmt.Expression is not InvocationExpressionSyntax inv) continue;
            ProcessChain(inv, semantic, parsed, report);
        }

        return parsed;
    }

    /// <summary>
    /// Decomposes a fluent chain <c>b.Foo().Bar().Baz()</c> into an ordered list
    /// of (methodName, invocation) pairs, innermost first, then dispatches each
    /// to the matching handler.
    /// </summary>
    private static void ProcessChain(
        InvocationExpressionSyntax outermost,
        SemanticModel semantic,
        Parsed parsed,
        System.Action<Diagnostic> report)
    {
        var calls = new List<InvocationExpressionSyntax>();
        InvocationExpressionSyntax? cur = outermost;
        while (cur is not null)
        {
            calls.Add(cur);
            if (cur.Expression is MemberAccessExpressionSyntax ma && ma.Expression is InvocationExpressionSyntax nextInv)
                cur = nextInv;
            else
                cur = null;
        }
        calls.Reverse();

        // First call is the "anchor": b.TypeScript(...), b.OpenApi(...), or b.ForType<T>().
        var first = calls[0];
        var firstName = GetMethodName(first.Expression);
        if (firstName is null) { ReportUnknown(report, first); return; }

        switch (firstName)
        {
            case "TypeScript":
                ApplyLambdaBlock(first, semantic, parsed.Settings.TypeScript, report,
                    (settings, prop, val) => AssignTypeScript(settings, prop, val));
                if (calls.Count > 1) ReportUnknown(report, calls[1]); // no chaining after global block
                return;

            case "OpenApi":
                ApplyLambdaBlock(first, semantic, parsed.Settings.OpenApi, report,
                    (settings, prop, val) => AssignOpenApi(settings, prop, val));
                if (calls.Count > 1) ReportUnknown(report, calls[1]);
                return;

            case "Python":
                ApplyLambdaBlock(first, semantic, parsed.Settings.Python, report,
                    (settings, prop, val) => AssignPython(settings, prop, val));
                if (calls.Count > 1) ReportUnknown(report, calls[1]);
                return;

            case "ForType":
                var typeName = ResolveForTypeArg(first, semantic);
                // Silent skip when the type symbol can't be resolved — almost always a partial
                // compilation in the IDE (mid-edit), not actually-broken code. The full build's
                // generator pass will succeed and pick this up. Reporting TG0012 here would
                // spam the editor with false positives that disappear on the next keystroke.
                if (typeName is null) return;
                if (!parsed.PerType.TryGetValue(typeName, out var overrides))
                    parsed.PerType[typeName] = overrides = new PerTypeOverrides();
                ProcessForTypeChain(calls, semantic, overrides, report);
                return;

            default:
                ReportUnknown(report, first);
                return;
        }
    }

    private static string? GetMethodName(ExpressionSyntax expr) => expr switch
    {
        MemberAccessExpressionSyntax ma => ma.Name switch
        {
            GenericNameSyntax g => g.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null,
        },
        _ => null,
    };

    private static string? ResolveForTypeArg(InvocationExpressionSyntax inv, SemanticModel sm)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax g }) return null;
        if (g.TypeArgumentList.Arguments.Count != 1) return null;
        var typeSyntax = g.TypeArgumentList.Arguments[0];
        var typeSym = sm.GetTypeInfo(typeSyntax).Type;
        return typeSym?.ToDisplayString();
    }

    // ── lambda body walker ─────────────────────────────────────────────────

    private static void ApplyLambdaBlock<T>(
        InvocationExpressionSyntax inv,
        SemanticModel sm,
        T target,
        System.Action<Diagnostic> report,
        System.Action<T, string, object?> assign)
    {
        if (inv.ArgumentList.Arguments.Count != 1) return;
        if (inv.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda) return;

        IEnumerable<StatementSyntax> stmts = lambda.Body switch
        {
            BlockSyntax block => block.Statements,
            ExpressionSyntax expr => new[] { SyntaxFactory.ExpressionStatement(expr) },
            _ => System.Array.Empty<StatementSyntax>(),
        };

        foreach (var stmt in stmts)
        {
            if (stmt is not ExpressionStatementSyntax es) continue;
            if (es.Expression is not AssignmentExpressionSyntax assignment) continue;
            if (assignment.Left is not MemberAccessExpressionSyntax left) continue;
            var propName = left.Name.Identifier.Text;
            var val = ReadLiteralValue(assignment.Right, sm);
            if (val is NonLiteralMarker)
            {
                report(Diagnostic.Create(
                    TypeGenDiagnostics.NonLiteralArgument,
                    assignment.Right.GetLocation(),
                    propName));
                continue;
            }
            assign(target, propName, val);
        }
    }

    private sealed class NonLiteralMarker { public static readonly NonLiteralMarker Instance = new(); }

    private static object? ReadLiteralValue(ExpressionSyntax expr, SemanticModel sm)
    {
        // ConstantValue covers string/numeric/bool literals and `const` field refs.
        var cv = sm.GetConstantValue(expr);
        if (cv.HasValue) return cv.Value;

        // Enum member access: TypeScriptFileLayout.SingleFile → underlying int.
        if (expr is MemberAccessExpressionSyntax ma)
        {
            var sym = sm.GetSymbolInfo(ma).Symbol as IFieldSymbol;
            if (sym is { IsConst: true, ConstantValue: { } v }) return v;
            if (sym?.ContainingType?.TypeKind == TypeKind.Enum && sym.HasConstantValue)
                return sym.ConstantValue;
        }

        return NonLiteralMarker.Instance;
    }

    // ── assign: maps lambda-body property names to internal mirror settings ─

    private static void AssignTypeScript(TypeScriptSettings s, string prop, object? val)
    {
        switch (prop)
        {
            case "OutputDir": s.OutputDir = val as string; break;
            case "SingleFileName": if (val is string sf) s.SingleFileName = sf; break;
            case "FileLayout": if (val is int fl) s.FileLayout = (TypeScriptFileLayout)fl; break;
            case "UseInterfaces": if (val is bool ui) s.UseInterfaces = ui; break;
            case "PropertyNameStyle": if (val is int pn) s.PropertyNameStyle = (NameStyle)pn; break;
            case "TypeNameStyle": if (val is int tn) s.TypeNameStyle = (NameStyle)tn; break;
            case "EmitGeneratedBanner": if (val is bool egb) s.EmitGeneratedBanner = egb; break;
            // StripSuffixes is a collection — not supported via simple assignment; users
            // need the collection initializer form which this parser doesn't walk (yet).
        }
    }

    private static void AssignOpenApi(OpenApiSettings s, string prop, object? val)
    {
        switch (prop)
        {
            case "OutputPath": if (val is string op) s.OutputPath = op; break;
            case "Title": if (val is string ti) s.Title = ti; break;
            case "Version": if (val is string v) s.Version = v; break;
            case "Description": s.Description = val as string; break;
            case "OpenApiVersion": if (val is string ov) s.OpenApiVersion = ov; break;
        }
    }

    private static void AssignPython(PythonSettings s, string prop, object? val)
    {
        switch (prop)
        {
            case "OutputDir": s.OutputDir = val as string; break;
            case "SingleFileName": if (val is string sf) s.SingleFileName = sf; break;
            case "FileLayout": if (val is int fl) s.FileLayout = (PythonFileLayout)fl; break;
            case "Style": if (val is int ps) s.Style = (PythonStyle)ps; break;
            case "SnakeCaseProperties": if (val is bool sc) s.SnakeCaseProperties = sc; break;
            case "EmitGeneratedBanner": if (val is bool egb) s.EmitGeneratedBanner = egb; break;
        }
    }

    // ── per-type chain calls (state machine for type ↔ property context) ───

    /// <summary>
    /// Walks the chain after <c>b.ForType&lt;T&gt;()</c>. Tracks "currently
    /// selected property" — calls before the first <c>.Property(...)</c> apply
    /// to the type; calls after apply to the most recent property until another
    /// <c>.Property(...)</c> switches context.
    /// </summary>
    private static void ProcessForTypeChain(
        List<InvocationExpressionSyntax> calls,
        SemanticModel sm,
        PerTypeOverrides typeOverrides,
        System.Action<Diagnostic> report)
    {
        PerPropertyOverrides? currentProp = null;
        for (int i = 1; i < calls.Count; i++)
        {
            var inv = calls[i];
            var name = GetMethodName(inv.Expression);
            if (name is null) { ReportUnknown(report, inv); continue; }

            if (name == "Property")
            {
                var propName = ResolvePropertySelector(inv, sm);
                if (propName is null) { ReportUnknown(report, inv); currentProp = null; continue; }
                if (!typeOverrides.Properties.TryGetValue(propName, out currentProp))
                    typeOverrides.Properties[propName] = currentProp = new PerPropertyOverrides();
                continue;
            }

            if (currentProp is null)
                ApplyTypeLevelCall(inv, name, sm, typeOverrides, report);
            else
                ApplyPropertyLevelCall(inv, name, sm, currentProp, report);
        }
    }

    private static void ApplyTypeLevelCall(
        InvocationExpressionSyntax inv,
        string name,
        SemanticModel sm,
        PerTypeOverrides o,
        System.Action<Diagnostic> report)
    {
        string? arg = ReadStringArg(inv, name, sm, report);
        switch (name)
        {
            case "TsName": o.TsName = arg; break;
            case "OpenApiName": o.OpenApiName = arg; break;
            case "OutputDir": o.OutputDir = arg; break;
            case "Ignore": o.Ignore = true; break;
            case "TsIgnore": o.TsIgnore = true; break;
            case "OpenApiIgnore": o.OpenApiIgnore = true; break;
            default: ReportUnknown(report, inv); break;
        }
    }

    private static void ApplyPropertyLevelCall(
        InvocationExpressionSyntax inv,
        string name,
        SemanticModel sm,
        PerPropertyOverrides o,
        System.Action<Diagnostic> report)
    {
        string? arg = ReadStringArg(inv, name, sm, report);
        switch (name)
        {
            case "TsName": o.TsName = arg; break;
            case "TsType": o.TsType = arg; break;
            case "OpenApiName": o.OpenApiName = arg; break;
            case "OpenApiType": o.OpenApiType = arg; break;
            case "OpenApiRef": o.OpenApiRef = arg; break;
            case "OpenApiFormat": o.OpenApiFormat = arg; break;
            case "OpenApiDescription": o.OpenApiDescription = arg; break;
            case "OpenApiNullable":
                var v = ReadLiteralValue(inv.ArgumentList.Arguments[0].Expression, sm);
                if (v is bool b) o.OpenApiNullable = b;
                else if (v is NonLiteralMarker) report(Diagnostic.Create(
                    TypeGenDiagnostics.NonLiteralArgument,
                    inv.ArgumentList.Arguments[0].GetLocation(), name));
                break;
            case "Ignore": o.Ignore = true; break;
            case "TsIgnore": o.TsIgnore = true; break;
            case "OpenApiIgnore": o.OpenApiIgnore = true; break;
            default: ReportUnknown(report, inv); break;
        }
    }

    private static string? ReadStringArg(
        InvocationExpressionSyntax inv, string name, SemanticModel sm, System.Action<Diagnostic> report)
    {
        if (inv.ArgumentList.Arguments.Count == 0) return null;
        var v = ReadLiteralValue(inv.ArgumentList.Arguments[0].Expression, sm);
        if (v is NonLiteralMarker)
        {
            report(Diagnostic.Create(
                TypeGenDiagnostics.NonLiteralArgument,
                inv.ArgumentList.Arguments[0].GetLocation(),
                name));
            return null;
        }
        return v as string;
    }

    /// <summary>
    /// Decodes <c>.Property(c =&gt; c.Email)</c> by walking the lambda body. Only
    /// simple member access on the lambda parameter is recognised — <c>c.X.Y</c>
    /// or method calls return null and surface as <c>TG0012</c>.
    /// </summary>
    private static string? ResolvePropertySelector(InvocationExpressionSyntax inv, SemanticModel sm)
    {
        if (inv.ArgumentList.Arguments.Count == 0) return null;
        if (inv.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda) return null;
        ExpressionSyntax? body = lambda.Body switch
        {
            ExpressionSyntax e => e,
            BlockSyntax block when block.Statements.Count == 1
                && block.Statements[0] is ReturnStatementSyntax ret => ret.Expression,
            _ => null,
        };
        if (body is not MemberAccessExpressionSyntax ma) return null;
        if (ma.Expression is not IdentifierNameSyntax) return null;   // reject c.X.Y
        return ma.Name.Identifier.Text;
    }

    private static void ReportUnknown(System.Action<Diagnostic> report, InvocationExpressionSyntax inv) =>
        report(Diagnostic.Create(
            TypeGenDiagnostics.UnknownConfiguratorCall,
            inv.GetLocation(),
            inv.ToString()));
}
