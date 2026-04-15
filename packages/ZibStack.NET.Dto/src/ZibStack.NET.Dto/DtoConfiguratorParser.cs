using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZibStack.NET.Shared;

namespace ZibStack.NET.Dto;

/// <summary>
/// Reads the user's <c>IDtoConfigurator</c> implementation by walking the
/// <c>Configure(IDtoBuilder b)</c> method body — never invokes anything at runtime.
/// Output feeds the same downstream extraction as the attribute path: each
/// <c>b.ForType&lt;T&gt;().CreateDto()</c> call equates to <c>[CreateDto]</c> on T,
/// per-property fluent overrides equate to <c>[DtoIgnore]</c> / <c>[DtoOnly]</c> /
/// <c>[DtoName]</c> / <c>[Flatten]</c>.
///
/// <para>
/// Mirrors ZibStack.NET.TypeGen's ConfiguratorParser shape so the two stay
/// recognizable to anyone touching either side.
/// </para>
/// </summary>
internal static class DtoConfiguratorParser
{
    private const string ConfiguratorInterface = "ZibStack.NET.Dto.IDtoConfigurator";

    /// <summary>Per-type configuration extracted from the fluent DSL.</summary>
    internal sealed class TypeConfig
    {
        public string TypeFullName { get; set; } = "";

        /// <summary>
        /// Symbol resolved at parse time — the downstream extraction needs it to
        /// walk properties. Held only for the duration of a single generator run;
        /// don't compare across compilations.
        /// </summary>
        public INamedTypeSymbol? Symbol { get; set; }

        // Variant flags — same as the attribute markers they replace.
        public bool Create { get; set; }
        public bool Update { get; set; }
        public bool CreateOrUpdate { get; set; }
        public bool Response { get; set; }
        public bool Query { get; set; }

        // Per-variant options (filled when the user passes a configure lambda).
        public string? CreateName { get; set; }
        public string? CreateValidatorTypeName { get; set; }
        public string? UpdateName { get; set; }
        public string? UpdateValidatorTypeName { get; set; }
        public string? CreateOrUpdateName { get; set; }
        public string? CreateOrUpdateCreateValidator { get; set; }
        public string? CreateOrUpdateUpdateValidator { get; set; }
        public string? ResponseName { get; set; }
        public string? QueryName { get; set; }
        public bool QuerySortable { get; set; } = true;
        public string? QueryDefaultSort { get; set; }
        public int QueryDefaultSortDirection { get; set; }    // SortDirection enum int

        // CrudApi settings — applied even when [CrudApi] is on the class as the marker.
        public bool HasCrudApiBlock { get; set; }
        public string? CrudRoute { get; set; }
        public string? CrudRoutePrefix { get; set; }
        public string CrudKeyProperty { get; set; } = "Id";
        public int CrudOperations { get; set; } = unchecked((int)0xFFFFFFFF);   // sentinel "not set"
        public int CrudStyle { get; set; }   // ApiStyle enum int
        public string? CrudAuthorizePolicy { get; set; }
        public string? CrudGetByIdPolicy { get; set; }
        public string? CrudGetListPolicy { get; set; }
        public string? CrudCreatePolicy { get; set; }
        public string? CrudUpdatePolicy { get; set; }
        public string? CrudDeletePolicy { get; set; }

        public Dictionary<string, PropertyConfig> Properties { get; } = new();
    }

    /// <summary>Per-property configuration from <c>b.ForType&lt;T&gt;().Property(p =&gt; p.X)...</c>.</summary>
    internal sealed class PropertyConfig
    {
        public bool Ignore { get; set; }              // unconditional ignore (no targets)
        public int IgnoreTargets { get; set; }        // bitmask, DtoTarget flags
        public int OnlyTargets { get; set; }          // bitmask
        public string? RenameTo { get; set; }
        public bool Flatten { get; set; }
    }

    public sealed class Parsed
    {
        public Dictionary<string, TypeConfig> ByType { get; } = new();
    }

    public static Parsed? Parse(Compilation compilation, System.Action<Diagnostic> report)
    {
        var iface = compilation.GetTypeByMetadataName(ConfiguratorInterface);
        if (iface is null) return null;

        // Find the user's IDtoConfigurator implementation.
        INamedTypeSymbol? configurator = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var sym = model.GetDeclaredSymbol(decl) as INamedTypeSymbol;
                if (sym is null) continue;
                if (sym.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iface)))
                {
                    configurator = sym;
                    break;
                }
            }
            if (configurator is not null) break;
        }
        if (configurator is null) return null;

        var configureMethod = configurator.GetMembers("Configure").OfType<IMethodSymbol>().FirstOrDefault();
        if (configureMethod is null) return null;
        var methodSyntax = configureMethod.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodSyntax?.Body is null) return null;

        var semantic = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
        var parsed = new Parsed();

        foreach (var stmt in methodSyntax.Body.Statements)
        {
            if (stmt is not ExpressionStatementSyntax es) continue;
            if (es.Expression is not InvocationExpressionSyntax outermost) continue;
            ProcessChain(outermost, semantic, parsed);
        }
        return parsed;
    }

    /// <summary>
    /// Decomposes the chain into innermost-first call list, dispatches based on the
    /// anchor (<c>b.ForType&lt;T&gt;()</c>), then walks the remaining calls switching
    /// between type-level and property-level context as <c>.Property(...)</c> appears.
    /// </summary>
    private static void ProcessChain(InvocationExpressionSyntax outermost, SemanticModel sm, Parsed parsed)
    {
        var calls = new List<InvocationExpressionSyntax>();
        InvocationExpressionSyntax? cur = outermost;
        while (cur is not null)
        {
            calls.Add(cur);
            if (cur.Expression is MemberAccessExpressionSyntax ma && ma.Expression is InvocationExpressionSyntax next)
                cur = next;
            else cur = null;
        }
        calls.Reverse();

        var first = calls[0];
        if (GetMethodName(first.Expression) != "ForType") return;

        var (typeName, typeSym) = ResolveForTypeArgAndSymbol(first, sm);
        if (typeName is null) return;

        if (!parsed.ByType.TryGetValue(typeName, out var tc))
            parsed.ByType[typeName] = tc = new TypeConfig { TypeFullName = typeName, Symbol = typeSym };
        if (tc.Symbol is null) tc.Symbol = typeSym;

        PropertyConfig? currentProp = null;
        for (int i = 1; i < calls.Count; i++)
        {
            var inv = calls[i];
            var name = GetMethodName(inv.Expression);
            if (name == "Property")
            {
                var propName = ResolvePropertySelector(inv);
                if (propName is null) { currentProp = null; continue; }
                if (!tc.Properties.TryGetValue(propName, out currentProp))
                    tc.Properties[propName] = currentProp = new PropertyConfig();
                continue;
            }
            if (currentProp is null)
                ApplyTypeLevel(inv, name, sm, tc);
            else
                ApplyPropertyLevel(inv, name, currentProp);
        }
    }

    private static void ApplyTypeLevel(InvocationExpressionSyntax inv, string? name, SemanticModel sm, TypeConfig tc)
    {
        switch (name)
        {
            case "CreateDto":
                tc.Create = true;
                ApplyOptionsLambda(inv, sm, (prop, val) =>
                {
                    if (prop == "Name" && val is string s) tc.CreateName = s;
                    else if (prop == "Validator" && val is INamedTypeSymbol t) tc.CreateValidatorTypeName = t.ToDisplayString();
                });
                break;
            case "UpdateDto":
                tc.Update = true;
                ApplyOptionsLambda(inv, sm, (prop, val) =>
                {
                    if (prop == "Name" && val is string s) tc.UpdateName = s;
                    else if (prop == "Validator" && val is INamedTypeSymbol t) tc.UpdateValidatorTypeName = t.ToDisplayString();
                });
                break;
            case "CreateOrUpdateDto":
                tc.CreateOrUpdate = true;
                ApplyOptionsLambda(inv, sm, (prop, val) =>
                {
                    if (prop == "Name" && val is string s) tc.CreateOrUpdateName = s;
                    else if (prop == "CreateValidator" && val is INamedTypeSymbol c) tc.CreateOrUpdateCreateValidator = c.ToDisplayString();
                    else if (prop == "UpdateValidator" && val is INamedTypeSymbol u) tc.CreateOrUpdateUpdateValidator = u.ToDisplayString();
                });
                break;
            case "ResponseDto":
                tc.Response = true;
                ApplyOptionsLambda(inv, sm, (prop, val) =>
                {
                    if (prop == "Name" && val is string s) tc.ResponseName = s;
                });
                break;
            case "QueryDto":
                tc.Query = true;
                ApplyOptionsLambda(inv, sm, (prop, val) =>
                {
                    if (prop == "Name" && val is string s) tc.QueryName = s;
                    else if (prop == "Sortable" && val is bool b) tc.QuerySortable = b;
                    else if (prop == "DefaultSort" && val is string ds) tc.QueryDefaultSort = ds;
                    else if (prop == "DefaultSortDirection" && val is int dir) tc.QueryDefaultSortDirection = dir;
                });
                break;
            case "CrudApi":
                tc.HasCrudApiBlock = true;
                ApplyOptionsLambda(inv, sm, (prop, val) =>
                {
                    switch (prop)
                    {
                        case "Route": tc.CrudRoute = val as string; break;
                        case "RoutePrefix": tc.CrudRoutePrefix = val as string; break;
                        case "KeyProperty": if (val is string kp && kp.Length > 0) tc.CrudKeyProperty = kp; break;
                        case "Operations": if (val is int o) tc.CrudOperations = o; break;
                        case "Style": if (val is int st) tc.CrudStyle = st; break;
                        case "AuthorizePolicy": tc.CrudAuthorizePolicy = val as string; break;
                        case "GetByIdPolicy": tc.CrudGetByIdPolicy = val as string; break;
                        case "GetListPolicy": tc.CrudGetListPolicy = val as string; break;
                        case "CreatePolicy": tc.CrudCreatePolicy = val as string; break;
                        case "UpdatePolicy": tc.CrudUpdatePolicy = val as string; break;
                        case "DeletePolicy": tc.CrudDeletePolicy = val as string; break;
                    }
                });
                break;
        }
    }

    private static void ApplyPropertyLevel(InvocationExpressionSyntax inv, string? name, PropertyConfig pc)
    {
        switch (name)
        {
            case "Ignore": pc.Ignore = true; pc.IgnoreTargets = (int)DtoTarget.All; break;
            case "IgnoreIn": pc.IgnoreTargets = ReadIntArg(inv); break;
            case "OnlyIn": pc.OnlyTargets = ReadIntArg(inv); break;
            case "RenameTo":
                if (inv.ArgumentList.Arguments.Count > 0
                    && inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
                    && lit.Token.Value is string s) pc.RenameTo = s;
                break;
            case "Flatten": pc.Flatten = true; break;
        }
    }

    private static int ReadIntArg(InvocationExpressionSyntax inv)
    {
        if (inv.ArgumentList.Arguments.Count == 0) return 0;
        // Could be a literal int, named const, or `DtoTarget.Create | DtoTarget.Update` — walk the expression and accumulate.
        return EvalIntExpr(inv.ArgumentList.Arguments[0].Expression);
    }

    /// <summary>
    /// Evaluates a flag expression composed of enum members and bitwise OR — supports
    /// <c>DtoTarget.Create</c>, <c>DtoTarget.Create | DtoTarget.Update</c>, plain int
    /// literals, and parenthesized forms. Anything else returns 0 (silently — a parser
    /// can't enforce arbitrary const-folding).
    /// </summary>
    private static int EvalIntExpr(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case ParenthesizedExpressionSyntax p: return EvalIntExpr(p.Expression);
            case LiteralExpressionSyntax lit when lit.Token.Value is int i: return i;
            case BinaryExpressionSyntax bin when bin.OperatorToken.Text == "|":
                return EvalIntExpr(bin.Left) | EvalIntExpr(bin.Right);
            case MemberAccessExpressionSyntax ma:
                // Match enum member by name → DtoTarget value.
                return ma.Name.Identifier.Text switch
                {
                    "None" => 0,
                    "Create" => 1,
                    "Update" => 2,
                    "Response" => 4,
                    "Query" => 8,
                    "List" => 16,
                    "All" => (int)DtoTarget.All,
                    _ => 0,
                };
            default: return 0;
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

    private static (string? Name, INamedTypeSymbol? Symbol) ResolveForTypeArgAndSymbol(InvocationExpressionSyntax inv, SemanticModel sm)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax g }) return (null, null);
        if (g.TypeArgumentList.Arguments.Count != 1) return (null, null);
        var typeSyntax = g.TypeArgumentList.Arguments[0];
        var typeSym = sm.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
        return (typeSym?.ToDisplayString(), typeSym);
    }

    /// <summary>
    /// Walks <c>p =&gt; p.PropertyName</c> selectors. Only single-member access on the
    /// lambda parameter is supported — no nested paths, no method calls.
    /// </summary>
    private static string? ResolvePropertySelector(InvocationExpressionSyntax inv)
    {
        if (inv.ArgumentList.Arguments.Count == 0) return null;
        if (inv.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda) return null;
        ExpressionSyntax? body = lambda.Body switch
        {
            ExpressionSyntax e => e,
            BlockSyntax block when block.Statements.Count == 1 && block.Statements[0] is ReturnStatementSyntax ret => ret.Expression,
            _ => null,
        };
        if (body is not MemberAccessExpressionSyntax ma) return null;
        if (ma.Expression is not IdentifierNameSyntax) return null;
        return ma.Name.Identifier.Text;
    }

    /// <summary>
    /// For calls like <c>.CreateDto(opts =&gt; opts.Name = "X")</c> — walks the lambda
    /// body's assignment statements, calling <paramref name="apply"/> for each
    /// <c>opts.Property = literal</c> pair found.
    /// </summary>
    private static void ApplyOptionsLambda(InvocationExpressionSyntax inv, SemanticModel sm, System.Action<string, object?> apply)
    {
        if (inv.ArgumentList.Arguments.Count == 0) return;
        if (inv.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda) return;

        IEnumerable<StatementSyntax> stmts = lambda.Body switch
        {
            BlockSyntax block => block.Statements,
            ExpressionSyntax expr => new[] { Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ExpressionStatement(expr) },
            _ => System.Array.Empty<StatementSyntax>(),
        };

        foreach (var stmt in stmts)
        {
            if (stmt is not ExpressionStatementSyntax es) continue;
            if (es.Expression is not AssignmentExpressionSyntax assignment) continue;
            if (assignment.Left is not MemberAccessExpressionSyntax left) continue;
            var propName = left.Name.Identifier.Text;
            var val = ReadValue(assignment.Right, sm);
            apply(propName, val);
        }
    }

    private static object? ReadValue(ExpressionSyntax expr, SemanticModel sm)
    {
        // Constants (string, int, bool literals + named consts).
        var cv = sm.GetConstantValue(expr);
        if (cv.HasValue) return cv.Value;

        // Enum members → underlying int (we cast at the call site).
        if (expr is MemberAccessExpressionSyntax ma)
        {
            var sym = sm.GetSymbolInfo(ma).Symbol as IFieldSymbol;
            if (sym?.ContainingType?.TypeKind == TypeKind.Enum && sym.HasConstantValue)
                return sym.ConstantValue is int i ? i : System.Convert.ToInt32(sym.ConstantValue);
        }

        // typeof(X) — used by Validator options.
        if (expr is TypeOfExpressionSyntax to)
            return sm.GetTypeInfo(to.Type).Type;

        return null;
    }
}
