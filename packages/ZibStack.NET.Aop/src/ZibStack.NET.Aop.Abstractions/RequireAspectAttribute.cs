using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Architectural rule: every concrete subclass (or implementor, for an interface) of the
/// annotated type must also carry the named aspect attribute. The
/// <c>ZibStack.NET.Aop.Analyzers.RequireAspectAnalyzer</c> reports diagnostic
/// <c>AOP1001</c> on derivatives that don't satisfy the rule, plus a code fix that
/// inserts the missing attribute.
///
/// <para>
/// Use this when a base type or interface only behaves correctly when wrapped by a
/// specific aspect (for example, a <c>Topic</c> base class that must be audited via
/// <c>[Log]</c>, or a <c>ICommandHandler</c> that must emit OpenTelemetry spans via
/// <c>[Trace]</c>). Without the attribute, the framework would still compile and run,
/// but with the silent edge case the author wants to prevent.
/// </para>
///
/// <para>
/// The rule is purely a compile-time hint emitted by the analyzer — there is no runtime
/// behavior attached. The attribute is itself <c>Inherited = true</c>, so derivatives of
/// derivatives also see the requirement; abstract types are exempt because the rule is
/// about concrete usage sites.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [RequireAspect(typeof(LogAttribute), Reason = "All Topics must be audited")]
/// public abstract class Topic { }
///
/// public class OrderPlaced : Topic { }    // ⚠ AOP1001 — missing [Log]
///
/// [Log]
/// public class PaymentMade : Topic { }    // ✅ ok
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
public sealed class RequireAspectAttribute : Attribute
{
    /// <summary>
    /// The aspect attribute type that derivatives must carry. Should be a subclass of
    /// <see cref="AspectAttribute"/>, but the analyzer doesn't enforce that — any
    /// attribute type works (so the same primitive can be reused for non-aspect
    /// "must have attribute X" rules without a separate API).
    /// </summary>
    public Type AspectType { get; }

    /// <summary>
    /// Human-readable explanation surfaced in the analyzer message. Optional but
    /// strongly recommended — "why" matters more than "what" when the diagnostic
    /// fires for a developer who didn't write the base type.
    /// </summary>
    public string? Reason { get; set; }

    public RequireAspectAttribute(Type aspectType) =>
        AspectType = aspectType ?? throw new ArgumentNullException(nameof(aspectType));
}
