namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Checks authorization before method execution. Throws <see cref="UnauthorizedAccessException"/>
/// if the check fails. Configure the authorization logic via <see cref="AuthorizeHandler.AuthorizationCheck"/>.
/// </summary>
/// <example>
/// <code>
/// // Setup (once, at startup):
/// AuthorizeHandler.AuthorizationCheck = (ctx, policy) =>
/// {
///     var user = GetCurrentUser();
///     return user.HasPermission(policy ?? ctx.MethodName);
/// };
///
/// [Authorize]
/// public void DeleteOrder(int id) { ... }
///
/// [Authorize(Policy = "Admin")]
/// public void PurgeAllData() { ... }
/// </code>
/// </example>
[AspectHandler(typeof(AuthorizeHandler))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AuthorizeAttribute : AspectAttribute
{
    /// <summary>Optional policy name. If null, the method name is used.</summary>
    public string? Policy { get; set; }
}

public sealed class AuthorizeHandler : IAroundAspectHandler
{
    /// <summary>
    /// Set this at startup. Parameters: AspectContext, policy string (nullable).
    /// Return true to allow, false to deny.
    /// </summary>
    public static Func<AspectContext, string?, bool>? AuthorizationCheck { get; set; }

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var policy = context.Properties.TryGetValue("Policy", out var p) && p is string s ? s : null;

        var check = AuthorizationCheck;
        if (check != null && !check(context, policy))
            throw new UnauthorizedAccessException(
                $"Access denied to {context.ClassName}.{context.MethodName}" +
                (policy != null ? $" (policy: {policy})" : ""));

        return proceed();
    }
}
