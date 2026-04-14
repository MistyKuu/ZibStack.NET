using System;
using System.Threading.Tasks;
using System.Transactions;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="TransactionAttribute"/>. Wraps method execution
/// in a <see cref="TransactionScope"/> with <see cref="TransactionScopeAsyncFlowOption.Enabled"/>.
/// </summary>
public sealed class TransactionHandler : IAroundAspectHandler, IAsyncAroundAspectHandler
{
    /// <inheritdoc />
    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var options = BuildOptions(context);

        using var scope = new TransactionScope(
            TransactionScopeOption.Required, options, TransactionScopeAsyncFlowOption.Enabled);

        var result = proceed();
        scope.Complete();
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<object?> AroundAsync(AspectContext context, Func<ValueTask<object?>> proceed)
    {
        var options = BuildOptions(context);

        using var scope = new TransactionScope(
            TransactionScopeOption.Required, options, TransactionScopeAsyncFlowOption.Enabled);

        var result = await proceed().ConfigureAwait(false);
        scope.Complete();
        return result;
    }

    private static TransactionOptions BuildOptions(AspectContext context)
    {
        var isolationLevel = IsolationLevel.ReadCommitted;
        var timeoutSeconds = 30;

        if (context.Properties.TryGetValue("IsolationLevel", out var il) && il is int i)
            isolationLevel = (IsolationLevel)i;
        if (context.Properties.TryGetValue("TimeoutSeconds", out var ts) && ts is int t)
            timeoutSeconds = t;

        return new TransactionOptions
        {
            IsolationLevel = isolationLevel,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
    }
}
