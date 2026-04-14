using System;
using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="AuthorizeAttribute"/>. Resolves <see cref="IAuthorizationProvider"/>
/// from DI and blocks method execution with <see cref="UnauthorizedAccessException"/> when
/// authorization fails.
///
///
/// <para>
/// Registered automatically by <c>AddAop()</c> as a singleton. Requires an
/// <see cref="IAuthorizationProvider"/> implementation registered in DI.
/// </para>
/// </summary>
public sealed class AuthorizeHandler : IAsyncAspectHandler
{
    private readonly IAuthorizationProvider _provider;

    public AuthorizeHandler(IAuthorizationProvider provider) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public async ValueTask OnBeforeAsync(AspectContext context)
    {
        string? policy = null;
        string? roles = null;

        if (context.Properties.TryGetValue("Policy", out var p) && p is string pol && pol.Length > 0) policy = pol;
        if (context.Properties.TryGetValue("Roles", out var r) && r is string rol && rol.Length > 0) roles = rol;

        if (policy is not null)
        {
            if (!await _provider.IsAuthorizedAsync(policy).ConfigureAwait(false))
                throw new UnauthorizedAccessException(
                    $"Authorization policy '{policy}' failed for {context.ClassName}.{context.MethodName}.");
            return;
        }

        if (roles is not null)
        {
            var roleList = roles.Split(',');
            foreach (var role in roleList)
            {
                var trimmed = role.Trim();
                if (trimmed.Length > 0 && await _provider.IsInRoleAsync(trimmed).ConfigureAwait(false))
                    return;
            }

            throw new UnauthorizedAccessException(
                $"None of the required roles '{roles}' matched for {context.ClassName}.{context.MethodName}.");
        }

        // No policy or roles specified — [Authorize] without args means "must be authenticated".
        if (!await _provider.IsAuthorizedAsync("__authenticated").ConfigureAwait(false))
            throw new UnauthorizedAccessException(
                $"Authentication required for {context.ClassName}.{context.MethodName}.");
    }

    /// <inheritdoc />
    public ValueTask OnAfterAsync(AspectContext context) => default;

    /// <inheritdoc />
    public ValueTask OnExceptionAsync(AspectContext context, Exception exception) => default;
}
