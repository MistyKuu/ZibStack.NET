using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Architectural rule: every concrete derivative of the annotated type must declare
/// (or inherit and not abstract-override) a method matching the configured signature.
/// Reported by the analyzer as <c>AOP1003</c>.
///
/// <para>
/// Plug-in / module systems often call methods by name via reflection or convention
/// (think <c>void Configure(IServiceCollection)</c>, <c>Task RunAsync()</c>, etc.).
/// The framework cannot enforce the contract through inheritance because the method
/// signature varies per host. <see cref="RequireMethodAttribute"/> closes that gap by
/// declaring the convention right next to the abstract base.
/// </para>
///
/// <para>
/// The rule is purely a compile-time hint emitted by the analyzer — no runtime behavior.
/// Apply multiple times to require multiple methods. Abstract derivatives are exempt
/// because the rule is about concrete usage sites.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [RequireMethod("Configure", ReturnType = typeof(void),
///                Parameters = new[] { typeof(IServiceCollection) },
///                Reason = "Modules must register their services")]
/// public abstract class Module { }
///
/// public class AuthModule : Module { }                                  // ⚠ AOP1003 — missing Configure
/// public class OrderModule : Module
/// {
///     public void Configure(IServiceCollection services) { }            // ✅
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
public sealed class RequireMethodAttribute : Attribute
{
    /// <summary>Method name that must be declared on the derivative.</summary>
    public string Name { get; }

    /// <summary>
    /// Required return type. When unset (default), any return type satisfies the rule.
    /// </summary>
    public Type? ReturnType { get; set; }

    /// <summary>
    /// Required parameter types in order. When unset (default), parameter types are not
    /// checked — only the name (and return type, if specified) matter.
    /// </summary>
    public Type[]? Parameters { get; set; }

    /// <summary>Optional human-readable rationale shown in the analyzer message.</summary>
    public string? Reason { get; set; }

    public RequireMethodAttribute(string name) =>
        Name = string.IsNullOrEmpty(name)
            ? throw new ArgumentNullException(nameof(name))
            : name;
}
