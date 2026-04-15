using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.CodeFixes;

/// <summary>
/// One generic code fix covering every AOP0030–AOP0041 numeric-argument diagnostic
/// (Polly + HybridCache add-on packages). Each diagnostic ID maps to a (property
/// name, default value, label) tuple via the table below; the actual rewrite is the
/// same for all of them — replace the offending literal with the package's documented
/// default.
///
/// <para>
/// Bundled into the same nupkg as the rest of the AOP analyzers / code fixes; if a
/// consumer doesn't reference the optional package, the relevant analyzer just never
/// fires and these registrations are inert.
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class FixOptionalAspectArgCodeFix : CodeFixProvider
{
    /// <summary>
    /// Diagnostic ID → (named-arg property, default value, action title).
    /// Using `object` for the value so we can carry both <see cref="int"/> and
    /// <see cref="double"/> defaults through one lookup.
    /// </summary>
    private static readonly Dictionary<string, (string Property, object Default, string Title)> Map = new()
    {
        // [PollyRetry]
        [Diagnostics.PollyRetryMaxAttemptsId] = ("MaxRetryAttempts", 3,   "Set MaxRetryAttempts to 3 (PollyRetry default)"),
        [Diagnostics.PollyRetryDelayId]       = ("DelayMs",          200, "Set DelayMs to 200 (PollyRetry default)"),

        // [HttpRetry]
        [Diagnostics.HttpRetryMaxAttemptsId]  = ("MaxRetryAttempts", 3,   "Set MaxRetryAttempts to 3 (HttpRetry default)"),
        [Diagnostics.HttpRetryDelayId]        = ("DelayMs",          200, "Set DelayMs to 200 (HttpRetry default)"),

        // [PollyCircuitBreaker]
        [Diagnostics.PollyCircuitBreakerThresholdId]  = ("FailureThreshold",         0.5, "Set FailureThreshold to 0.5 (default)"),
        [Diagnostics.PollyCircuitBreakerThroughputId] = ("MinimumThroughput",        10,  "Set MinimumThroughput to 10 (default)"),
        [Diagnostics.PollyCircuitBreakerSamplingId]   = ("SamplingDurationSeconds",  30,  "Set SamplingDurationSeconds to 30 (default)"),
        [Diagnostics.PollyCircuitBreakerBreakId]      = ("BreakDurationSeconds",     15,  "Set BreakDurationSeconds to 15 (default)"),

        // [PollyRateLimiter]
        [Diagnostics.PollyRateLimiterPermitsId] = ("PermitLimit",   100, "Set PermitLimit to 100 (default)"),
        [Diagnostics.PollyRateLimiterWindowId]  = ("WindowSeconds", 60,  "Set WindowSeconds to 60 (default)"),
        [Diagnostics.PollyRateLimiterQueueId]   = ("QueueLimit",    0,   "Set QueueLimit to 0 (reject overflow immediately)"),

        // [HybridCache]
        [Diagnostics.HybridCacheDurationId] = ("DurationSeconds", 300, "Set DurationSeconds to 300 (default)"),
    };

    public override ImmutableArray<string> FixableDiagnosticIds => Map.Keys.ToImmutableArray();

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        if (!Map.TryGetValue(diagnostic.Id, out var entry)) return;

        var attr = root.FindNode(diagnostic.Location.SourceSpan)
            .AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        var arg = attr is null ? null : SetAttributeArgumentHelper.FindNamedArg(attr, entry.Property);
        if (arg is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: entry.Title,
                createChangedDocument: ct => SetAsync(context.Document, arg, entry.Default, ct),
                equivalenceKey: $"AOP_{diagnostic.Id}_default"),
            diagnostic);
    }

    private static async Task<Document> SetAsync(Document document, AttributeArgumentSyntax arg, object value, System.Threading.CancellationToken ct)
    {
        var literal = value switch
        {
            int i  => SyntaxFactory.Literal(i),
            // Double literals: keep an explicit "0.5" form so the rewritten code reads
            // naturally next to a probability-style property like FailureThreshold.
            double d => SyntaxFactory.Literal(d.ToString(System.Globalization.CultureInfo.InvariantCulture), d),
            _ => SyntaxFactory.Literal(0),
        };
        return await SetAttributeArgumentHelper.SetNumericArgAsync(document, arg, literal, ct).ConfigureAwait(false);
    }
}
