namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Checks authorization before method execution via <see cref="IPermissionChecker"/> from DI.
/// Throws <see cref="UnauthorizedAccessException"/> if denied.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddScoped&lt;IPermissionChecker, MyAuthChecker&gt;();
/// builder.Services.AddTransient&lt;RequirePermissionHandler&gt;();
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
    public string? Policy { get; set; }
}

/// <summary>
/// Register in DI to provide authorization logic for <see cref="RequirePermissionAttribute"/>.
/// </summary>
public interface IPermissionChecker
{
    bool HasPermission(AspectContext context, string? policy);
}

public sealed class RequirePermissionHandler : IAroundAspectHandler
{
    private readonly IPermissionChecker _checker;

    public RequirePermissionHandler(IPermissionChecker checker) => _checker = checker;

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var policy = context.Properties.TryGetValue("Policy", out var p) && p is string s ? s : null;

        if (!_checker.HasPermission(context, policy))
            throw new UnauthorizedAccessException(
                $"Access denied to {context.ClassName}.{context.MethodName}" +
                (policy != null ? $" (policy: {policy})" : ""));

        return proceed();
    }
}
