using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Architectural rule: every concrete derivative of the annotated type must declare a
/// public instance constructor matching the configured parameter list. Reported by the
/// analyzer as <c>AOP1004</c>.
///
/// <para>
/// Use this when a base class can only be activated correctly with specific dependencies
/// — typically DI scenarios where the framework new's-up the derivative via an injected
/// service — and you want a compile-time signal instead of the runtime
/// "no matching constructor" exception that DI would otherwise throw.
/// </para>
///
/// <para>
/// Pass an empty parameter list to require a parameterless ctor: <c>[RequireConstructor]</c>.
/// Apply multiple times to require alternative shapes — the rule is satisfied as long as
/// the derivative exposes ONE matching constructor.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [RequireConstructor(typeof(IServiceProvider),
///                     Reason = "Plugins are activated by the host with the request scope")]
/// public abstract class Plugin { }
///
/// public class GoodPlugin : Plugin
/// {
///     public GoodPlugin(IServiceProvider sp) { }     // ✅
/// }
///
/// public class BrokenPlugin : Plugin
/// {
///     public BrokenPlugin() { }                       // ⚠ AOP1004 — missing ctor (IServiceProvider)
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
public sealed class RequireConstructorAttribute : Attribute
{
    /// <summary>
    /// Parameter types the required constructor must accept, in order. Empty array means
    /// the rule requires a parameterless (default) constructor.
    /// </summary>
    public Type[] ParameterTypes { get; }

    /// <summary>Optional human-readable rationale shown in the analyzer message.</summary>
    public string? Reason { get; set; }

    public RequireConstructorAttribute(params Type[] parameterTypes) =>
        ParameterTypes = parameterTypes ?? Array.Empty<Type>();
}
