namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Checks authorization before method execution. Throws <see cref="UnauthorizedAccessException"/>
/// if the check fails. Configure via DI (<see cref="IPermissionChecker"/>) or
/// static <see cref="RequirePermissionHandler.AuthorizationCheck"/>.
/// </summary>
/// <example>
/// <code>
/// // Option 1 — DI (recommended):
/// builder.Services.AddScoped&lt;IPermissionChecker, MyAuthChecker&gt;();
/// builder.Services.AddTransient&lt;RequirePermissionHandler&gt;();
///
/// // Option 2 — static delegate:
/// RequirePermissionHandler.AuthorizationCheck = (ctx, policy) =&gt;
/// {
///     var user = GetCurrentUser();
///     return user.HasPermission(policy ?? ctx.MethodName);
/// };
///
/// [RequirePermission]
/// public void DeleteOrder(int id) { ... }
///
/// [RequirePermission(Policy = "Admin")]
/// public void PurgeAllData() { ... }
/// </code>
/// </example>
[AspectHandler(typeof(RequirePermissionHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequirePermissionAttribute : AspectAttribute
{
    /// <summary>Optional policy name. If null, the method name is used.</summary>
    public string? Policy { get; set; }
}

/// <summary>
/// Authorization checker interface. Register in DI to provide authorization logic.
/// </summary>
public interface IPermissionChecker
{
    /// <summary>Return true to allow, false to deny.</summary>
    bool HasPermission(AspectContext context, string? policy);
}

public sealed class RequirePermissionHandler : IAroundAspectHandler
{
    private readonly IPermissionChecker? _checker;

    /// <summary>Static delegate fallback. Used when DI is not configured.</summary>
    public static Func<AspectContext, string?, bool>? AuthorizationCheck { get; set; }

    public RequirePermissionHandler() { }

    public RequirePermissionHandler(IPermissionChecker checker) => _checker = checker;

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var policy = context.Properties.TryGetValue("Policy", out var p) && p is string s ? s : null;

        bool authorized;
        if (_checker != null)
            authorized = _checker.HasPermission(context, policy);
        else
            authorized = AuthorizationCheck?.Invoke(context, policy) ?? true;

        if (!authorized)
            throw new UnauthorizedAccessException(
                $"Access denied to {context.ClassName}.{context.MethodName}" +
                (policy != null ? $" (policy: {policy})" : ""));

        return proceed();
    }
}
