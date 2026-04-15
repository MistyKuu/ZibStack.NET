using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Architectural rule: every concrete derivative of the annotated type must also
/// implement the named interface. Reported by the analyzer as <c>AOP1002</c>, with a
/// code fix that inserts the interface into the derivative's base list.
///
/// <para>
/// Use this when a base type's contract assumes a separate capability — for example,
/// <c>DatabaseConnection</c> needing <c>IDisposable</c> for cleanup, or every
/// <c>BackgroundService</c> needing <c>IAsyncDisposable</c>. The base type can't
/// directly inherit the interface (it'd be forced on every member), so the rule
/// catches the moment a derivative forgets it.
/// </para>
///
/// <para>
/// The rule is purely a compile-time hint emitted by the analyzer — no runtime
/// behavior. Apply multiple times for multiple required interfaces. Abstract
/// derivatives are exempt because the rule is about concrete usage sites.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [RequireImplementation(typeof(IDisposable), Reason = "Connections must clean up sockets")]
/// public abstract class DatabaseConnection { }
///
/// public class SqlConnection : DatabaseConnection { }              // ⚠ AOP1002 — missing IDisposable
/// public class PgConnection : DatabaseConnection, IDisposable { void Dispose() { } }   // ✅
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
public sealed class RequireImplementationAttribute : Attribute
{
    /// <summary>The interface that derivatives must implement.</summary>
    public Type InterfaceType { get; }

    /// <summary>Optional human-readable rationale shown in the analyzer message.</summary>
    public string? Reason { get; set; }

    public RequireImplementationAttribute(Type interfaceType) =>
        InterfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));
}
