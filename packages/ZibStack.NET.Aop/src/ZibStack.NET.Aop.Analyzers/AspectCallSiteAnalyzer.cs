using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Diagnoses call sites that look like they should fire an aspect but won't, because
/// of how C# interceptors work:
///
/// <list type="bullet">
///   <item>Method groups converted to delegates (<c>Func&lt;...&gt;</c>, <c>Action&lt;...&gt;</c>,
///         event handlers) capture the original method directly. The interceptor is only
///         consulted at the literal call site of the method, not when the delegate is invoked.</item>
///   <item><c>base.Method()</c> deliberately uses <c>call</c> (not <c>callvirt</c>) and skips
///         interceptors that target the call expression — even when the method has an aspect.</item>
/// </list>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AspectCallSiteAnalyzer : DiagnosticAnalyzer
{
    private const string AspectAttributeFullName = "ZibStack.NET.Aop.AspectAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Diagnostics.DelegateConversion,
            Diagnostics.BaseCall);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    // ── AOP0020: method group → delegate ───────────────────────────────────

    private static void AnalyzeMethodReference(OperationAnalysisContext ctx)
    {
        var op = (IMethodReferenceOperation)ctx.Operation;
        var method = op.Method;
        if (method is null) return;

        // Only flag method group conversions that actually create a delegate.
        // Roslyn wraps these in IDelegateCreationOperation; nameof(...) does not.
        if (op.Parent is not IDelegateCreationOperation) return;

        if (!HasAspect(method)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.DelegateConversion,
            op.Syntax.GetLocation(),
            method.Name));
    }

    // ── AOP0021: base.Method() invocation ──────────────────────────────────

    private static void AnalyzeInvocation(OperationAnalysisContext ctx)
    {
        var op = (IInvocationOperation)ctx.Operation;
        var method = op.TargetMethod;
        if (method is null) return;

        if (!HasAspect(method)) return;

        // The instance receiver expression must be `base` for this to be a base call.
        // Roslyn surfaces base receivers as IInstanceReferenceOperation with kind ContainingTypeInstance,
        // but the simplest reliable signal is the syntax: BaseExpressionSyntax inside a MemberAccessExpression.
        if (op.Syntax is not InvocationExpressionSyntax inv) return;
        if (inv.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Expression is not BaseExpressionSyntax) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.BaseCall,
            inv.GetLocation(),
            method.Name));
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static bool HasAspect(IMethodSymbol method)
    {
        if (HasAnyAspectAttribute(method.GetAttributes())) return true;
        if (method.ContainingType is { } ct && HasAnyAspectAttribute(ct.GetAttributes())) return true;
        return false;
    }

    private static bool HasAnyAspectAttribute(System.Collections.Generic.IEnumerable<AttributeData> attributes)
    {
        foreach (var a in attributes)
        {
            if (DerivesFromAspectAttribute(a.AttributeClass))
                return true;
        }
        return false;
    }

    private static bool DerivesFromAspectAttribute(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == AspectAttributeFullName)
                return true;
        }
        return false;
    }
}
