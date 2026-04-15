using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Architectural rule: the annotated type may only be referenced from code whose
/// containing namespace matches one of the configured patterns. Reported by the
/// analyzer as <c>AOP1005</c> on the offending call site (object creation, method
/// invocation on the type, or static-member access).
///
/// <para>
/// This is the "convention enforcement" counterpart of <see cref="RequireAspectAttribute"/>:
/// instead of saying "every derivative must do X", it says "only callers in scope X may
/// touch this type at all". Use it for internal-but-public helpers (sealed-off engine
/// types you can't realistically make C# <c>internal</c> because consumers in the same
/// assembly need them too), test-only constructors, or for carving out a private API
/// surface inside a single library.
/// </para>
///
/// <para>
/// Pattern syntax — keep it simple:
/// <list type="bullet">
///   <item><c>"MyApp.Internal"</c> — exact namespace match only</item>
///   <item><c>"MyApp.Internal.**"</c> — that namespace plus any sub-namespace</item>
/// </list>
/// Apply multiple times to allow several scopes (rule satisfied if ANY pattern matches
/// the call-site namespace).
/// </para>
/// </summary>
/// <example>
/// <code>
/// [ScopeTo("MyApp.Internal.**", Reason = "Engine bypass — public API is in MyApp.Public")]
/// public class SecretEngine
/// {
///     public void DoMagic() { }
/// }
///
/// // namespace MyApp.Internal.Things
/// var e = new SecretEngine();        // ✅
///
/// // namespace MyApp.Public.Api
/// var e = new SecretEngine();        // ⚠ AOP1005 — call site outside allowed scope
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Method,
    Inherited = false,
    AllowMultiple = true)]
public sealed class ScopeToAttribute : Attribute
{
    /// <summary>
    /// Namespace pattern. Either an exact namespace (<c>"MyApp.Internal"</c>) or a
    /// prefix with <c>".**"</c> suffix to also match sub-namespaces
    /// (<c>"MyApp.Internal.**"</c>).
    /// </summary>
    public string Namespace { get; }

    /// <summary>Optional human-readable rationale shown in the analyzer message.</summary>
    public string? Reason { get; set; }

    public ScopeToAttribute(string @namespace) =>
        Namespace = string.IsNullOrEmpty(@namespace)
            ? throw new ArgumentNullException(nameof(@namespace))
            : @namespace;
}
