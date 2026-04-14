using System;
using System.Transactions;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that wraps method execution in a <see cref="TransactionScope"/>.
/// Commits on success, rolls back on exception.
///
/// <para>
/// Uses <see cref="TransactionScopeAsyncFlowOption.Enabled"/> so it works correctly
/// with async methods.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Transaction]
/// public void TransferFunds(int fromId, int toId, decimal amount) { ... }
///
/// [Transaction(IsolationLevel = IsolationLevel.ReadCommitted, TimeoutSeconds = 30)]
/// public async Task&lt;Order&gt; PlaceOrderAsync(OrderRequest req) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(TransactionHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TransactionAttribute : AspectAttribute
{
    /// <summary>
    /// Transaction isolation level. Default: <see cref="System.Transactions.IsolationLevel.ReadCommitted"/>.
    /// </summary>
    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Transaction timeout in seconds. Default: 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
