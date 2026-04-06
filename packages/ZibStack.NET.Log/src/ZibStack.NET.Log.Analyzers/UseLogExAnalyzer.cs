using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Log.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseLogExAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZLOG001";

    private static readonly LocalizableString Title = "Use LogXxxEx for interpolated strings";
    private static readonly LocalizableString MessageFormat = "Use '{0}' instead of '{1}' with interpolated strings for structured logging";
    private static readonly LocalizableString Description = "Standard ILogger.LogXxx methods with interpolated strings lose structured logging data. Use the ZibStack.NET.Log LogXxxEx extension methods instead, which preserve property names for structured logging sinks like Serilog/Seq.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    private static readonly Dictionary<string, string> MethodMapping = new()
    {
        { "LogTrace", "LogTraceEx" },
        { "LogDebug", "LogDebugEx" },
        { "LogInformation", "LogInformationEx" },
        { "LogWarning", "LogWarningEx" },
        { "LogError", "LogErrorEx" },
        { "LogCritical", "LogCriticalEx" },
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Must be a member access: logger.LogXxx(...)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;

        // Quick check: is it one of the standard log methods?
        if (!MethodMapping.TryGetValue(methodName, out var suggestedMethod))
            return;

        // Check if any argument is an interpolated string
        var hasInterpolatedArg = invocation.ArgumentList.Arguments
            .Any(arg => arg.Expression is InterpolatedStringExpressionSyntax);

        if (!hasInterpolatedArg)
            return;

        // Verify the method is on ILogger (from Microsoft.Extensions.Logging)
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var containingType = methodSymbol.ContainingType?.ToDisplayString();

        // Match both the extension class and direct ILogger calls
        if (containingType != "Microsoft.Extensions.Logging.LoggerExtensions"
            && !IsILoggerType(methodSymbol.ContainingType))
            return;

        var diagnostic = Diagnostic.Create(
            Rule,
            memberAccess.Name.GetLocation(),
            suggestedMethod,
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsILoggerType(INamedTypeSymbol? type)
    {
        if (type is null) return false;

        // Check if the type implements ILogger
        return type.AllInterfaces.Any(i =>
            i.ToDisplayString() == "Microsoft.Extensions.Logging.ILogger")
            || type.ToDisplayString() == "Microsoft.Extensions.Logging.ILogger";
    }
}
