using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Log.Analyzers;

/// <summary>
/// Flags legacy <c>logger.LogXxx("template {Param}", value)</c> calls and suggests
/// the structured <c>logger.LogXxx($"template {value}")</c> form. The interpolated
/// form is intercepted by the ZibStack.NET.Log source generator and dispatched via
/// a cached <c>LoggerMessage.Define&lt;T&gt;</c> delegate — same allocations as the
/// standard call but ~40x faster when the level is disabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseInterpolatedLogAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZLOG002";

    private static readonly LocalizableString Title =
        "Use interpolated string with LogXxx for zero-cost structured logging";

    private static readonly LocalizableString MessageFormat =
        "Replace '{0}(\"template\", args)' with '{0}($\"template\")' — the ZibStack.NET.Log generator emits a typed LoggerMessage.Define interceptor (same alloc, ~40x faster when level is disabled)";

    private static readonly LocalizableString Description =
        "Standard ILogger.LogXxx(\"template {Param}\", value) calls work but go through the runtime FormattedLogValues parser. The interpolated form $\"template {value}\" is intercepted by the ZibStack.NET.Log source generator and dispatched via a cached LoggerMessage.Define<T> delegate.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    private static readonly HashSet<string> LogMethodNames = new()
    {
        "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical",
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

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!LogMethodNames.Contains(methodName))
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return;

        // Find the message argument:
        //   logger.LogXxx("template", arg1, arg2)              → message = arg[0]
        //   logger.LogXxx(ex, "template", arg1, arg2)          → message = arg[1]
        //   logger.LogXxx(eventId, "template", arg1, arg2)     → message = arg[1]
        //   logger.LogXxx(eventId, ex, "template", arg1, arg2) → message = arg[2]
        // We just walk arguments and find the first string-literal positional arg
        // that is followed by at least one more argument.
        ArgumentSyntax? messageArg = null;
        int messageIdx = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                messageArg = args[i];
                messageIdx = i;
                break;
            }
        }
        if (messageArg is null) return;

        // Only flag when there are template args (otherwise plain message is fine).
        var trailingArgs = args.Count - messageIdx - 1;
        if (trailingArgs == 0) return;

        // Verify this is actually a Microsoft.Extensions.Logging extension call.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) return;

        var containingType = methodSymbol.ContainingType?.ToDisplayString();
        if (containingType != "Microsoft.Extensions.Logging.LoggerExtensions" &&
            !IsZibStructuredExtension(containingType))
        {
            // Could be on ILogger directly via some custom extension — check first param type
            if (methodSymbol.Parameters.Length == 0) return;
            var firstParamType = methodSymbol.Parameters[0].Type.ToDisplayString();
            if (firstParamType != "Microsoft.Extensions.Logging.ILogger") return;
        }

        // The receiver must be an ILogger so we don't flag unrelated LogXxx methods.
        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (receiverType is null) return;
        if (!ImplementsILogger(receiverType)) return;

        var diagnostic = Diagnostic.Create(
            Rule,
            memberAccess.Name.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsZibStructuredExtension(string? fqn) =>
        fqn == "ZibStack.NET.Log.LoggerStructuredExtensions";

    private static bool ImplementsILogger(ITypeSymbol type)
    {
        if (type.ToDisplayString() == "Microsoft.Extensions.Logging.ILogger") return true;
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == "Microsoft.Extensions.Logging.ILogger") return true;
            // ILogger<T> implements ILogger
            if (iface.OriginalDefinition.ToDisplayString() == "Microsoft.Extensions.Logging.ILogger<TCategoryName>") return true;
        }
        return false;
    }
}
