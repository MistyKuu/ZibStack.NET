using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Validates the arguments of built-in aspect attributes (<c>[Cache]</c>, <c>[Retry]</c>,
/// <c>[Timeout]</c>, <c>[Validate]</c>) against semantic constraints that the runtime
/// would either reject or silently no-op on. All diagnostics are reported on the attribute
/// application itself so the squiggle lands precisely on the wrong value.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BuiltInAspectArgumentAnalyzer : DiagnosticAnalyzer
{
    private const string CacheAttributeFullName = "ZibStack.NET.Aop.CacheAttribute";
    private const string RetryAttributeFullName = "ZibStack.NET.Aop.RetryAttribute";
    private const string TimeoutAttributeFullName = "ZibStack.NET.Aop.TimeoutAttribute";
    private const string ValidateAttributeFullName = "ZibStack.NET.Aop.ValidateAttribute";

    // Optional add-on packages — full-name strings only, so missing references just
    // produce no matches (no hard dependency on Polly / HybridCache).
    private const string PollyRetryAttributeFullName = "ZibStack.NET.Aop.PollyRetryAttribute";
    private const string HttpRetryAttributeFullName = "ZibStack.NET.Aop.HttpRetryAttribute";
    private const string PollyCircuitBreakerAttributeFullName = "ZibStack.NET.Aop.PollyCircuitBreakerAttribute";
    private const string PollyRateLimiterAttributeFullName = "ZibStack.NET.Aop.PollyRateLimiterAttribute";
    private const string HybridCacheAttributeFullName = "ZibStack.NET.Aop.HybridCacheAttribute";

    private const string CancellationTokenFullName = "System.Threading.CancellationToken";
    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";
    // ZibStack's own validation marker that should also count as "annotated".
    private const string ValidationAttributeFullName = "System.ComponentModel.DataAnnotations.ValidationAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Diagnostics.CacheNonReturning,
            Diagnostics.RetryMaxAttempts,
            Diagnostics.RetryDelay,
            Diagnostics.RetryBackoff,
            Diagnostics.TimeoutValue,
            Diagnostics.TimeoutNoCancellationToken,
            Diagnostics.ValidateNoParameters,
            Diagnostics.ValidateNoAnnotations,
            Diagnostics.PollyRetryMaxAttempts,
            Diagnostics.PollyRetryDelay,
            Diagnostics.HttpRetryMaxAttempts,
            Diagnostics.HttpRetryDelay,
            Diagnostics.PollyCircuitBreakerThreshold,
            Diagnostics.PollyCircuitBreakerThroughput,
            Diagnostics.PollyCircuitBreakerSampling,
            Diagnostics.PollyCircuitBreakerBreak,
            Diagnostics.PollyRateLimiterPermits,
            Diagnostics.PollyRateLimiterWindow,
            Diagnostics.PollyRateLimiterQueue,
            Diagnostics.HybridCacheDuration);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext ctx)
    {
        var method = (IMethodSymbol)ctx.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || method.IsStatic) return;

        foreach (var attr in method.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            switch (name)
            {
                case CacheAttributeFullName:
                    AnalyzeCache(ctx, method, attr);
                    break;
                case RetryAttributeFullName:
                    AnalyzeRetry(ctx, attr);
                    break;
                case TimeoutAttributeFullName:
                    AnalyzeTimeout(ctx, method, attr);
                    break;
                case ValidateAttributeFullName:
                    AnalyzeValidate(ctx, method, attr);
                    break;
                case PollyRetryAttributeFullName:
                    AnalyzePollyRetry(ctx, attr);
                    break;
                case HttpRetryAttributeFullName:
                    AnalyzeHttpRetry(ctx, attr);
                    break;
                case PollyCircuitBreakerAttributeFullName:
                    AnalyzePollyCircuitBreaker(ctx, attr);
                    break;
                case PollyRateLimiterAttributeFullName:
                    AnalyzePollyRateLimiter(ctx, attr);
                    break;
                case HybridCacheAttributeFullName:
                    AnalyzeHybridCache(ctx, attr);
                    break;
            }
        }
    }

    // ── [PollyRetry] / [HttpRetry] ─────────────────────────────────────────

    private static void AnalyzePollyRetry(SymbolAnalysisContext ctx, AttributeData attr)
    {
        var max = GetNamedArg<int>(attr, "MaxRetryAttempts");
        if (max.HasValue && max.Value < 1)
            ReportOnAttribute(ctx, Diagnostics.PollyRetryMaxAttempts, attr, max.Value);

        var delay = GetNamedArg<int>(attr, "DelayMs");
        if (delay.HasValue && delay.Value < 0)
            ReportOnAttribute(ctx, Diagnostics.PollyRetryDelay, attr, delay.Value);
    }

    private static void AnalyzeHttpRetry(SymbolAnalysisContext ctx, AttributeData attr)
    {
        var max = GetNamedArg<int>(attr, "MaxRetryAttempts");
        if (max.HasValue && max.Value < 1)
            ReportOnAttribute(ctx, Diagnostics.HttpRetryMaxAttempts, attr, max.Value);

        var delay = GetNamedArg<int>(attr, "DelayMs");
        if (delay.HasValue && delay.Value < 0)
            ReportOnAttribute(ctx, Diagnostics.HttpRetryDelay, attr, delay.Value);
    }

    // ── [PollyCircuitBreaker] ──────────────────────────────────────────────

    private static void AnalyzePollyCircuitBreaker(SymbolAnalysisContext ctx, AttributeData attr)
    {
        var threshold = GetNamedArg<double>(attr, "FailureThreshold");
        if (threshold.HasValue && (threshold.Value <= 0 || threshold.Value > 1))
            ReportOnAttribute(ctx, Diagnostics.PollyCircuitBreakerThreshold, attr, threshold.Value);

        var throughput = GetNamedArg<int>(attr, "MinimumThroughput");
        if (throughput.HasValue && throughput.Value < 1)
            ReportOnAttribute(ctx, Diagnostics.PollyCircuitBreakerThroughput, attr, throughput.Value);

        var sampling = GetNamedArg<int>(attr, "SamplingDurationSeconds");
        if (sampling.HasValue && sampling.Value <= 0)
            ReportOnAttribute(ctx, Diagnostics.PollyCircuitBreakerSampling, attr, sampling.Value);

        var brk = GetNamedArg<int>(attr, "BreakDurationSeconds");
        if (brk.HasValue && brk.Value <= 0)
            ReportOnAttribute(ctx, Diagnostics.PollyCircuitBreakerBreak, attr, brk.Value);
    }

    // ── [PollyRateLimiter] ──────────────────────────────────────────────────

    private static void AnalyzePollyRateLimiter(SymbolAnalysisContext ctx, AttributeData attr)
    {
        var permits = GetNamedArg<int>(attr, "PermitLimit");
        if (permits.HasValue && permits.Value < 1)
            ReportOnAttribute(ctx, Diagnostics.PollyRateLimiterPermits, attr, permits.Value);

        var window = GetNamedArg<int>(attr, "WindowSeconds");
        if (window.HasValue && window.Value <= 0)
            ReportOnAttribute(ctx, Diagnostics.PollyRateLimiterWindow, attr, window.Value);

        var queue = GetNamedArg<int>(attr, "QueueLimit");
        if (queue.HasValue && queue.Value < 0)
            ReportOnAttribute(ctx, Diagnostics.PollyRateLimiterQueue, attr, queue.Value);
    }

    // ── [HybridCache] ──────────────────────────────────────────────────────

    private static void AnalyzeHybridCache(SymbolAnalysisContext ctx, AttributeData attr)
    {
        var duration = GetNamedArg<int>(attr, "DurationSeconds");
        if (duration.HasValue && duration.Value < 0)
            ReportOnAttribute(ctx, Diagnostics.HybridCacheDuration, attr, duration.Value);
    }

    // ── [Cache] ────────────────────────────────────────────────────────────

    private static void AnalyzeCache(SymbolAnalysisContext ctx, IMethodSymbol method, AttributeData attr)
    {
        if (!IsValueReturningMethod(method))
        {
            ReportOnAttribute(ctx, Diagnostics.CacheNonReturning, attr, method.Name);
        }
    }

    /// <summary>
    /// True if the method actually produces a value worth caching: not void, not
    /// non-generic Task / non-generic ValueTask.
    /// </summary>
    private static bool IsValueReturningMethod(IMethodSymbol method)
    {
        if (method.ReturnsVoid) return false;
        if (method.ReturnType is INamedTypeSymbol nt)
        {
            var rtName = nt.OriginalDefinition.ToDisplayString();
            if (rtName is "System.Threading.Tasks.Task"
                       or "System.Threading.Tasks.ValueTask")
                return false;
        }
        return true;
    }

    // ── [Retry] ────────────────────────────────────────────────────────────

    private static void AnalyzeRetry(SymbolAnalysisContext ctx, AttributeData attr)
    {
        var maxAttempts = GetNamedArg<int>(attr, "MaxAttempts");
        if (maxAttempts.HasValue && maxAttempts.Value < 1)
        {
            ReportOnAttribute(ctx, Diagnostics.RetryMaxAttempts, attr, maxAttempts.Value);
        }

        var delayMs = GetNamedArg<int>(attr, "DelayMs");
        if (delayMs.HasValue && delayMs.Value < 0)
        {
            ReportOnAttribute(ctx, Diagnostics.RetryDelay, attr, delayMs.Value);
        }

        var backoff = GetNamedArg<double>(attr, "BackoffMultiplier");
        if (backoff.HasValue && backoff.Value < 1.0)
        {
            ReportOnAttribute(ctx, Diagnostics.RetryBackoff, attr, backoff.Value);
        }
    }

    // ── [Timeout] ──────────────────────────────────────────────────────────

    private static void AnalyzeTimeout(SymbolAnalysisContext ctx, IMethodSymbol method, AttributeData attr)
    {
        var timeoutMs = GetNamedArg<int>(attr, "TimeoutMs");
        if (timeoutMs.HasValue && timeoutMs.Value <= 0)
        {
            ReportOnAttribute(ctx, Diagnostics.TimeoutValue, attr, timeoutMs.Value);
        }

        // AOP0015 — verified honest after the TimeoutHandler rewrite. The handler now
        // uses ctx.CancellationTokenSource (wired by the generator) when the method
        // has a CancellationToken parameter; otherwise it falls back to
        // Task.WhenAny, which still aborts to the caller but leaks the body.
        var hasCt = method.Parameters.Any(p =>
            p.Type.ToDisplayString() == CancellationTokenFullName);
        if (!hasCt)
        {
            ReportOnAttribute(ctx, Diagnostics.TimeoutNoCancellationToken, attr, method.Name);
        }
    }

    // ── [Validate] ─────────────────────────────────────────────────────────

    private static void AnalyzeValidate(SymbolAnalysisContext ctx, IMethodSymbol method, AttributeData attr)
    {
        if (method.Parameters.Length == 0)
        {
            ReportOnAttribute(ctx, Diagnostics.ValidateNoParameters, attr, method.Name);
            return;
        }

        // Look for any DataAnnotation on the parameters themselves OR on their reachable
        // property graph. Bail at depth 3 to keep the search bounded for big graphs.
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        bool anyAnnotation = method.Parameters.Any(p =>
            HasAnyAnnotationAttribute(p.GetAttributes()) ||
            TypeHasAnyAnnotation(p.Type, visited, depth: 0));

        if (!anyAnnotation)
        {
            ReportOnAttribute(ctx, Diagnostics.ValidateNoAnnotations, attr, method.Name);
        }
    }

    private static bool TypeHasAnyAnnotation(ITypeSymbol type, HashSet<ITypeSymbol> visited, int depth)
    {
        if (depth > 3) return false;
        if (type is not INamedTypeSymbol named) return false;
        if (named.SpecialType != SpecialType.None) return false;
        if (!visited.Add(named)) return false;

        foreach (var member in named.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;

            if (HasAnyAnnotationAttribute(prop.GetAttributes()))
                return true;

            if (TypeHasAnyAnnotation(prop.Type, visited, depth + 1))
                return true;
        }
        return false;
    }

    private static bool HasAnyAnnotationAttribute(IEnumerable<AttributeData> attributes)
    {
        foreach (var a in attributes)
        {
            var ac = a.AttributeClass;
            if (ac is null) continue;

            // Direct DataAnnotations namespace (covers all Microsoft-provided attributes).
            if (ac.ContainingNamespace?.ToDisplayString() == DataAnnotationsNamespace)
                return true;

            // Any user attribute deriving from ValidationAttribute.
            for (var t = ac.BaseType; t is not null; t = t.BaseType)
            {
                if (t.ToDisplayString() == ValidationAttributeFullName)
                    return true;
            }
        }
        return false;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static T? GetNamedArg<T>(AttributeData attr, string name) where T : struct
    {
        foreach (var na in attr.NamedArguments)
        {
            if (na.Key == name && na.Value.Value is T v)
                return v;
        }
        return null;
    }

    private static void ReportOnAttribute(SymbolAnalysisContext ctx, DiagnosticDescriptor rule, AttributeData attr, params object[] args)
    {
        var loc = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
        if (loc is null) return;
        ctx.ReportDiagnostic(Diagnostic.Create(rule, loc, args));
    }
}
