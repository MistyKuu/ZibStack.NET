using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Architectural rule: where the attribute is placed determines what must carry the
/// named aspect.
/// <list type="bullet">
///   <item><b>On a class or interface</b> — every concrete derivative (subclass /
///         implementor) must also carry the aspect.</item>
///   <item><b>On a method</b> — every override / interface implementation of that
///         specific method must carry the aspect (or have it inherited from a
///         class-level aspect on the containing type).</item>
/// </list>
/// The <c>ZibStack.NET.Aop.Analyzers.RequireAspectAnalyzer</c> reports diagnostic
/// <c>AOP1001</c> on members that don't satisfy the rule, plus a code fix that inserts
/// the missing attribute.
///
/// <para>
/// Use this when a base type or interface (or a specific abstract / interface method)
/// only behaves correctly when wrapped by a particular aspect — for example, a
/// <c>Topic</c> base class that must be audited via <c>[Log]</c>, or an
/// <c>ICommandHandler.HandleAsync</c> that must emit OpenTelemetry spans via
/// <c>[Trace]</c>. Without the attribute, the framework would still compile and run,
/// just in the silent edge case the author wants to prevent.
/// </para>
///
/// <para>
/// The rule is purely a compile-time hint emitted by the analyzer — no runtime behavior
/// is attached. Abstract types and abstract methods are exempt because the rule is about
/// concrete usage sites. Class-level aspects on the derivative type satisfy method-level
/// requirements (the parser already propagates them to public/internal members).
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Class-level rule — derivative must have [Log]:
/// [RequireAspect(typeof(LogAttribute), Reason = "All Topics must be audited")]
/// public abstract class Topic { }
///
/// public class OrderPlaced : Topic { }    // ⚠ AOP1001 — missing [Log]
/// [Log] public class PaymentMade : Topic { }   // ✅
///
/// // Method-level rule — every implementation must have [Trace]:
/// public interface ICommandHandler
/// {
///     [RequireAspect(typeof(TraceAttribute))]
///     Task HandleAsync(object cmd);
/// }
///
/// public class CreateOrderHandler : ICommandHandler
/// {
///     public Task HandleAsync(object cmd) => ...;        // ⚠ missing [Trace]
/// }
///
/// public class CancelOrderHandler : ICommandHandler
/// {
///     [Trace] public Task HandleAsync(object cmd) => ...; // ✅
/// }
///
/// [Trace]
/// public class RefundOrderHandler : ICommandHandler
/// {
///     public Task HandleAsync(object cmd) => ...;         // ✅ class-level [Trace] satisfies
/// }
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = true)]
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
