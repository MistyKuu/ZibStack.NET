using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that performs authorization checks before method execution.
/// The handler resolves <see cref="IAuthorizationProvider"/> from DI to evaluate the check.
///
/// <para>
/// Use <see cref="Policy"/> for policy-based authorization (e.g. <c>"CanEditOrders"</c>)
/// or <see cref="Roles"/> for simple role checks (e.g. <c>"Admin,Manager"</c>).
/// When both are set, the policy check takes precedence.
/// </para>
///
/// <para>
/// If the authorization check fails, the handler throws <see cref="UnauthorizedAccessException"/>.
/// The built-in <see cref="AuthorizeHandler"/> is registered automatically by
/// <c>AddAop()</c>. You must register an <see cref="IAuthorizationProvider"/> implementation
/// in DI for the handler to work.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Register your provider:
/// builder.Services.AddSingleton&lt;IAuthorizationProvider, MyAuthProvider&gt;();
///
/// [Authorize(Roles = "Admin,Manager")]
/// public void DeleteOrder(int id) { ... }
///
/// [Authorize(Policy = "CanEditProducts")]
/// public Product UpdateProduct(int id, ProductDto dto) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(AuthorizeHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class AuthorizeAttribute : AspectAttribute
{
    /// <summary>
    /// Policy name to evaluate. When set, <see cref="IAuthorizationProvider.IsAuthorizedAsync"/>
    /// is called with this value. Takes precedence over <see cref="Roles"/>.
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// Comma-separated list of allowed roles (e.g. <c>"Admin,Manager"</c>).
    /// The user must have at least one of the listed roles.
    /// Ignored when <see cref="Policy"/> is set.
    /// </summary>
    public string? Roles { get; set; }
}
