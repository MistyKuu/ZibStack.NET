using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Implement this interface to provide authorization logic for <see cref="AuthorizeAttribute"/>.
/// Register your implementation in DI so the built-in <see cref="AuthorizeHandler"/> can resolve it.
/// </summary>
/// <example>
/// <code>
/// public class HttpContextAuthProvider : IAuthorizationProvider
/// {
///     private readonly IHttpContextAccessor _accessor;
///
///     public HttpContextAuthProvider(IHttpContextAccessor accessor) => _accessor = accessor;
///
///     public ValueTask&lt;bool&gt; IsAuthorizedAsync(string policy)
///     {
///         var user = _accessor.HttpContext?.User;
///         return new ValueTask&lt;bool&gt;(user?.HasClaim("policy", policy) == true);
///     }
///
///     public ValueTask&lt;bool&gt; IsInRoleAsync(string role)
///     {
///         var user = _accessor.HttpContext?.User;
///         return new ValueTask&lt;bool&gt;(user?.IsInRole(role) == true);
///     }
/// }
/// </code>
/// </example>
public interface IAuthorizationProvider
{
    /// <summary>
    /// Checks whether the current principal satisfies the given policy.
    /// </summary>
    ValueTask<bool> IsAuthorizedAsync(string policy);

    /// <summary>
    /// Checks whether the current principal has the given role.
    /// </summary>
    ValueTask<bool> IsInRoleAsync(string role);
}
