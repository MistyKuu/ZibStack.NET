using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// Extracts project-wide aspect defaults from <c>IAopConfigurator.Configure(IAopBuilder b)</c>.
/// The body is parsed as a compile-time DSL — never invoked at runtime.
///
/// <para>
/// Recognized shape:
/// <code>
/// b.Retry(r =>   { r.MaxAttempts = 5; r.DelayMs = 200; });
/// b.Timeout(t => { t.TimeoutMs = 10_000; });
/// </code>
/// Each section name maps to an attribute FQN; each property assigned inside the lambda
/// contributes one entry to that aspect's defaults dictionary. Anything Roslyn can treat
/// as a compile-time constant (literals, enum members, const fields, casts, unary ops)
/// is accepted.
/// </para>
/// </summary>
public static class AopConfiguratorParser
{
    private const string ConfiguratorInterfaceFqn = "ZibStack.NET.Aop.IAopConfigurator";

    // Section name (matching IAopBuilder method) → aspect attribute FQN.
    private static readonly Dictionary<string, string> SectionToAspect = new()
    {
        ["Retry"] = "ZibStack.NET.Aop.RetryAttribute",
        ["Timeout"] = "ZibStack.NET.Aop.TimeoutAttribute",
        ["Trace"] = "ZibStack.NET.Aop.TraceAttribute",
        ["Cache"] = "ZibStack.NET.Aop.CacheAttribute",
        ["Metrics"] = "ZibStack.NET.Aop.MetricsAttribute",
    };

    /// <summary>
    /// A parsed <c>b.Apply&lt;TAspect&gt;(to =&gt; ..., configure =&gt; ...)</c> rule.
    /// </summary>
    public sealed class ApplyRule
    {
        public string AspectFqn { get; set; } = "";
        public string? NamespacePrefix { get; set; }
        public string? ImplementingFqn { get; set; }
        public string? DerivedFromFqn { get; set; }
        public List<string> ExceptFqns { get; } = new();
        // ClassesWhere predicates — parsed from expression tree
        public string? ClassNameStartsWith { get; set; }
        public string? ClassNameEndsWith { get; set; }
        public string? ClassNameContains { get; set; }
        public bool? ClassIsAbstract { get; set; }
        public bool? ClassIsSealed { get; set; }
        // MethodsWhere predicates
        public string? MethodNameStartsWith { get; set; }
        public string? MethodNameEndsWith { get; set; }
        public string? MethodNameContains { get; set; }
        public bool? MethodIsAsync { get; set; }
        public bool? MethodIsPublic { get; set; }
        public bool? MethodIsStatic { get; set; }
        // Aspect configuration (property assignments)
        public Dictionary<string, object?> Properties { get; } = new();
    }

    public sealed class ParsedConfig
    {
        public Dictionary<string, IReadOnlyDictionary<string, object?>> Defaults { get; } = new();
        public List<ApplyRule> ApplyRules { get; } = new();
    }

    // Cache per Compilation.
    private static readonly ConditionalWeakTable<Compilation, ParsedConfig> _cache = new();

    /// <summary>
    /// Returns per-aspect defaults keyed by attribute FQN (e.g. <c>"ZibStack.NET.Aop.RetryAttribute" → { "MaxAttempts" = 5, ... }</c>).
    /// Empty dictionary when no configurator is found.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Read(Compilation compilation)
        => ReadAll(compilation).Defaults;

    /// <summary>Returns the full parsed config including Apply rules.</summary>
    public static ParsedConfig ReadAll(Compilation compilation)
    {
        if (_cache.TryGetValue(compilation, out var cached)) return cached;
        var result = ReadUncached(compilation);
        _cache.Add(compilation, result);
        return result;
    }

    private static ParsedConfig ReadUncached(Compilation compilation)
    {
        var config = new ParsedConfig();
        var iface = compilation.GetTypeByMetadataName(ConfiguratorInterfaceFqn);
        if (iface is null) return config;

        foreach (var t in EnumerateAllTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iface)))
                continue;

            var configure = t.GetMembers("Configure").OfType<IMethodSymbol>().FirstOrDefault();
            if (configure is null) continue;

            var syntaxRef = configure.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax() is not MethodDeclarationSyntax method || method.Body is null)
                continue;

            var semanticModel = compilation.GetSemanticModel(method.SyntaxTree);
            ParseBody(method.Body, semanticModel, config.Defaults);
            ParseApplyRules(method.Body, semanticModel, config.ApplyRules);
        }
        return config;
    }

    private static void ParseBody(
        BlockSyntax body,
        SemanticModel sm,
        Dictionary<string, IReadOnlyDictionary<string, object?>> result)
    {
        foreach (var stmt in body.Statements)
        {
            if (stmt is not ExpressionStatementSyntax es) continue;
            if (es.Expression is not InvocationExpressionSyntax inv) continue;
            if (inv.Expression is not MemberAccessExpressionSyntax mae) continue;
            var sectionName = mae.Name.Identifier.Text;
            if (!SectionToAspect.TryGetValue(sectionName, out var aspectFqn)) continue;
            if (inv.ArgumentList.Arguments.Count != 1) continue;
            if (inv.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda)
                continue;
            if (lambda.Body is not BlockSyntax lambdaBody) continue;

            var sectionDict = new Dictionary<string, object?>();
            if (result.TryGetValue(aspectFqn, out var existing))
                foreach (var kv in existing) sectionDict[kv.Key] = kv.Value;

            foreach (var lstmt in lambdaBody.Statements)
            {
                if (lstmt is not ExpressionStatementSyntax les) continue;
                if (les.Expression is not AssignmentExpressionSyntax assign) continue;
                if (assign.Left is not MemberAccessExpressionSyntax lma) continue;

                var propName = lma.Name.Identifier.Text;
                var constant = sm.GetConstantValue(assign.Right);
                if (!constant.HasValue) continue;

                sectionDict[propName] = constant.Value;
            }
            result[aspectFqn] = sectionDict;
        }
    }

    /// <summary>
    /// Parses <c>b.Apply&lt;TAspect&gt;(to =&gt; to.Namespace("X").Implementing&lt;Y&gt;(), cfg =&gt; ...)</c>
    /// </summary>
    private static void ParseApplyRules(BlockSyntax body, SemanticModel sm, List<ApplyRule> rules)
    {
        foreach (var stmt in body.Statements)
        {
            if (stmt is not ExpressionStatementSyntax es) continue;
            if (es.Expression is not InvocationExpressionSyntax inv) continue;

            // Match b.Apply<TAspect>(...)
            if (inv.Expression is not MemberAccessExpressionSyntax mae) continue;
            if (mae.Name is not GenericNameSyntax gns || gns.Identifier.Text != "Apply") continue;
            if (gns.TypeArgumentList.Arguments.Count != 1) continue;

            // Resolve TAspect type argument
            var typeArgSyntax = gns.TypeArgumentList.Arguments[0];
            var typeInfo = sm.GetTypeInfo(typeArgSyntax);
            if (typeInfo.Type is not INamedTypeSymbol aspectType) continue;
            var aspectFqn = aspectType.ToDisplayString();

            var rule = new ApplyRule { AspectFqn = aspectFqn };
            var args = inv.ArgumentList.Arguments;

            // First arg: selector lambda  to => to.Namespace("X").Implementing<Y>()...
            if (args.Count >= 1 && args[0].Expression is SimpleLambdaExpressionSyntax selectorLambda)
                ParseSelectorChain(selectorLambda.Body, sm, rule);

            // Second arg (optional): configure lambda  c => c.DurationSeconds = 120
            if (args.Count >= 2 && args[1].Expression is SimpleLambdaExpressionSyntax configLambda)
                ParseConfigLambda(configLambda, sm, rule.Properties);

            rules.Add(rule);
        }
    }

    /// <summary>
    /// Walks the fluent chain: <c>to.Namespace("X").Implementing&lt;Y&gt;().PublicMethods()</c>
    /// Each method call is a selector; they're chained via the return value.
    /// </summary>
    private static void ParseSelectorChain(SyntaxNode body, SemanticModel sm, ApplyRule rule)
    {
        // The body is either a single expression (to => to.X().Y()) or a block.
        ExpressionSyntax? expr = body switch
        {
            ExpressionSyntax e => e,
            BlockSyntax block => (block.Statements.FirstOrDefault() as ExpressionStatementSyntax)?.Expression
                              ?? (block.Statements.FirstOrDefault() as ReturnStatementSyntax)?.Expression,
            _ => null,
        };
        if (expr is null) return;

        // Unwind the chain from outermost to innermost call.
        var calls = new List<InvocationExpressionSyntax>();
        var current = expr;
        while (current is InvocationExpressionSyntax call)
        {
            calls.Add(call);
            current = (call.Expression as MemberAccessExpressionSyntax)?.Expression;
        }

        foreach (var call in calls)
        {
            if (call.Expression is not MemberAccessExpressionSyntax memberAccess) continue;
            var methodName = memberAccess.Name.Identifier.Text;

            switch (methodName)
            {
                case "Namespace":
                    if (call.ArgumentList.Arguments.Count == 1)
                    {
                        var cv = sm.GetConstantValue(call.ArgumentList.Arguments[0].Expression);
                        if (cv.HasValue && cv.Value is string ns) rule.NamespacePrefix = ns;
                    }
                    break;

                case "Implementing":
                    if (memberAccess.Name is GenericNameSyntax implGns && implGns.TypeArgumentList.Arguments.Count == 1)
                    {
                        var ti = sm.GetTypeInfo(implGns.TypeArgumentList.Arguments[0]);
                        if (ti.Type != null) rule.ImplementingFqn = ti.Type.ToDisplayString();
                    }
                    break;

                case "DerivedFrom":
                    if (memberAccess.Name is GenericNameSyntax derGns && derGns.TypeArgumentList.Arguments.Count == 1)
                    {
                        var ti = sm.GetTypeInfo(derGns.TypeArgumentList.Arguments[0]);
                        if (ti.Type != null) rule.DerivedFromFqn = ti.Type.ToDisplayString();
                    }
                    break;

                case "Except":
                    if (memberAccess.Name is GenericNameSyntax excGns && excGns.TypeArgumentList.Arguments.Count == 1)
                    {
                        var ti = sm.GetTypeInfo(excGns.TypeArgumentList.Arguments[0]);
                        if (ti.Type != null) rule.ExceptFqns.Add(ti.Type.ToDisplayString());
                    }
                    break;

                case "PublicMethods":
                    rule.MethodIsPublic = true;
                    break;

                case "ClassesWhere":
                    if (call.ArgumentList.Arguments.Count == 1)
                        ParsePredicateLambda(call.ArgumentList.Arguments[0].Expression, sm, rule, isClass: true);
                    break;

                case "MethodsWhere":
                    if (call.ArgumentList.Arguments.Count == 1)
                        ParsePredicateLambda(call.ArgumentList.Arguments[0].Expression, sm, rule, isClass: false);
                    break;
            }
        }
    }

    /// <summary>
    /// Parses simple predicates from lambda expressions like:
    /// <c>c => c.Name.StartsWith("Order")</c>, <c>m => m.IsAsync</c>,
    /// <c>m => m.IsAsync &amp;&amp; m.IsPublic</c>
    /// </summary>
    private static void ParsePredicateLambda(ExpressionSyntax arg, SemanticModel sm, ApplyRule rule, bool isClass)
    {
        // Unwrap the lambda: Expression<Func<ClassInfo/MethodInfo, bool>> or just Func
        ExpressionSyntax? body = arg switch
        {
            SimpleLambdaExpressionSyntax sle => sle.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax ple => ple.Body as ExpressionSyntax,
            _ => null,
        };
        if (body is null) return;

        ParsePredicateExpression(body, sm, rule, isClass);
    }

    private static void ParsePredicateExpression(ExpressionSyntax expr, SemanticModel sm, ApplyRule rule, bool isClass)
    {
        // Handle && (BinaryExpression with LogicalAnd)
        if (expr is BinaryExpressionSyntax binary && binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression))
        {
            ParsePredicateExpression(binary.Left, sm, rule, isClass);
            ParsePredicateExpression(binary.Right, sm, rule, isClass);
            return;
        }

        // Handle: x.Name.StartsWith("prefix")
        if (expr is InvocationExpressionSyntax inv
            && inv.Expression is MemberAccessExpressionSyntax mae
            && inv.ArgumentList.Arguments.Count == 1)
        {
            var methodName = mae.Name.Identifier.Text;
            var cv = sm.GetConstantValue(inv.ArgumentList.Arguments[0].Expression);
            if (cv.HasValue && cv.Value is string strVal && mae.Expression is MemberAccessExpressionSyntax prop)
            {
                var propName = prop.Name.Identifier.Text;
                if (propName == "Name")
                {
                    switch (methodName)
                    {
                        case "StartsWith":
                            if (isClass) rule.ClassNameStartsWith = strVal;
                            else rule.MethodNameStartsWith = strVal;
                            break;
                        case "EndsWith":
                            if (isClass) rule.ClassNameEndsWith = strVal;
                            else rule.MethodNameEndsWith = strVal;
                            break;
                        case "Contains":
                            if (isClass) rule.ClassNameContains = strVal;
                            else rule.MethodNameContains = strVal;
                            break;
                    }
                }
            }
            return;
        }

        // Handle: x.IsAsync, x.IsPublic, etc. (simple member access = true)
        if (expr is MemberAccessExpressionSyntax simpleProp)
        {
            var name = simpleProp.Name.Identifier.Text;
            if (isClass)
            {
                switch (name)
                {
                    case "IsAbstract": rule.ClassIsAbstract = true; break;
                    case "IsSealed": rule.ClassIsSealed = true; break;
                }
            }
            else
            {
                switch (name)
                {
                    case "IsAsync": rule.MethodIsAsync = true; break;
                    case "IsPublic": rule.MethodIsPublic = true; break;
                    case "IsStatic": rule.MethodIsStatic = true; break;
                }
            }
            return;
        }

        // Handle: !x.IsStatic (PrefixUnaryExpression with LogicalNot)
        if (expr is PrefixUnaryExpressionSyntax { RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalNotExpression } neg
            && neg.Operand is MemberAccessExpressionSyntax negProp)
        {
            var name = negProp.Name.Identifier.Text;
            if (isClass)
            {
                switch (name)
                {
                    case "IsAbstract": rule.ClassIsAbstract = false; break;
                    case "IsSealed": rule.ClassIsSealed = false; break;
                }
            }
            else
            {
                switch (name)
                {
                    case "IsAsync": rule.MethodIsAsync = false; break;
                    case "IsPublic": rule.MethodIsPublic = false; break;
                    case "IsStatic": rule.MethodIsStatic = false; break;
                }
            }
        }
    }

    /// <summary>Parses property assignments from a configure lambda: <c>c => c.DurationSeconds = 120</c>.</summary>
    private static void ParseConfigLambda(SimpleLambdaExpressionSyntax lambda, SemanticModel sm, Dictionary<string, object?> props)
    {
        // Block body: c => { c.X = 1; c.Y = 2; }
        if (lambda.Body is BlockSyntax block)
        {
            foreach (var s in block.Statements)
            {
                if (s is not ExpressionStatementSyntax exprStmt) continue;
                if (exprStmt.Expression is not AssignmentExpressionSyntax assign) continue;
                if (assign.Left is not MemberAccessExpressionSyntax lma) continue;
                var cv = sm.GetConstantValue(assign.Right);
                if (cv.HasValue) props[lma.Name.Identifier.Text] = cv.Value;
            }
        }
        // Expression body: c => c.X = 1 (single assignment)
        else if (lambda.Body is AssignmentExpressionSyntax singleAssign
                 && singleAssign.Left is MemberAccessExpressionSyntax slma)
        {
            var cv = sm.GetConstantValue(singleAssign.Right);
            if (cv.HasValue) props[slma.Name.Identifier.Text] = cv.Value;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateAllTypes(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers())
        {
            yield return t;
            foreach (var nested in EnumerateNested(t)) yield return nested;
        }
        foreach (var sub in ns.GetNamespaceMembers())
            foreach (var t in EnumerateAllTypes(sub)) yield return t;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol t)
    {
        foreach (var n in t.GetTypeMembers())
        {
            yield return n;
            foreach (var nn in EnumerateNested(n)) yield return nn;
        }
    }
}
