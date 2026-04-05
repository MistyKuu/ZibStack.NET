namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Checks authorization before method execution. Throws <see cref="UnauthorizedAccessException"/>
/// if the check fails. Configure via DI (<see cref="IAuthorizationChecker"/>) or
/// static <see cref="AuthorizeHandler.AuthorizationCheck"/>.
/// </summary>
/// <example>
/// <code>
/// // Option 1 — DI (recommended):
/// builder.Services.AddScoped&lt;IAuthorizationChecker, MyAuthChecker&gt;();
/// builder.Services.AddTransient&lt;AuthorizeHandler&gt;();
///
/// // Option 2 — static delegate:
/// AuthorizeHandler.AuthorizationCheck = (ctx, policy) =&gt;
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

/// <summary>
/// Authorization checker interface. Register in DI to provide authorization logic.
/// </summary>
public interface IAuthorizationChecker
{
    /// <summary>Return true to allow, false to deny.</summary>
    bool IsAuthorized(AspectContext context, string? policy);
}

public sealed class AuthorizeHandler : IAroundAspectHandler
{
    private readonly IAuthorizationChecker? _checker;

    /// <summary>Static delegate fallback. Used when DI is not configured.</summary>
    public static Func<AspectContext, string?, bool>? AuthorizationCheck { get; set; }

    public AuthorizeHandler() { }

    public AuthorizeHandler(IAuthorizationChecker checker) => _checker = checker;

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var policy = context.Properties.TryGetValue("Policy", out var p) && p is string s ? s : null;

        bool authorized;
        if (_checker != null)
            authorized = _checker.IsAuthorized(context, policy);
        else
            authorized = AuthorizationCheck?.Invoke(context, policy) ?? true;

        if (!authorized)
            throw new UnauthorizedAccessException(
                $"Access denied to {context.ClassName}.{context.MethodName}" +
                (policy != null ? $" (policy: {policy})" : ""));

        return proceed();
    }
}
