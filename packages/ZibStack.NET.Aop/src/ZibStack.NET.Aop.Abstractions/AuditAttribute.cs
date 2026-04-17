namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that captures before/after state of method parameters on mutation
/// methods and writes an audit trail entry via <see cref="IAuditStore"/>.
///
/// <para>
/// On entry (<c>OnBefore</c>), the handler snapshots all non-<c>[NoLog]</c> parameters
/// as JSON. On exit (<c>OnAfter</c>), it snapshots them again (capturing mutations).
/// Both snapshots plus method metadata are written to the registered
/// <see cref="IAuditStore"/> implementation. Parameters marked <c>[Sensitive]</c>
/// are masked as <c>"***"</c> in both snapshots.
/// </para>
///
/// <para>
/// Register an <see cref="IAuditStore"/> in DI — without one, the handler silently
/// no-ops (no exception, no audit entry). The built-in handler is registered by
/// <c>AddAop()</c> automatically.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Audit]
/// public async Task&lt;Order&gt; UpdateOrderAsync(int id, UpdateOrderRequest request) { ... }
///
/// // With explicit action label:
/// [Audit(Action = "PlaceOrder")]
/// public async Task&lt;Order&gt; PlaceOrderAsync(CreateOrderRequest request) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(AuditHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AuditAttribute : AspectAttribute
{
    /// <summary>
    /// Custom action label for the audit entry (e.g. "PlaceOrder", "CancelSubscription").
    /// When null, defaults to the method name.
    /// </summary>
    public string? Action { get; set; }
}

/// <summary>
/// A single audit trail entry. Implementations of <see cref="IAuditStore"/> receive
/// this and persist it however they choose (DB table, event store, log sink, etc.).
/// </summary>
public sealed class AuditEntry
{
    /// <summary>UTC timestamp of the operation.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Class containing the audited method.</summary>
    public string ClassName { get; set; } = "";

    /// <summary>Audited method name.</summary>
    public string MethodName { get; set; } = "";

    /// <summary>Custom action label from <see cref="AuditAttribute.Action"/>, or method name if null.</summary>
    public string Action { get; set; } = "";

    /// <summary>Parameter snapshot BEFORE the method executed. Sensitive params masked.</summary>
    public string? BeforeSnapshot { get; set; }

    /// <summary>Parameter snapshot AFTER the method executed (captures mutations). Sensitive params masked.</summary>
    public string? AfterSnapshot { get; set; }

    /// <summary>Return value summary (ToString). Null for void methods or on exception.</summary>
    public string? ResultSummary { get; set; }

    /// <summary>True if the method threw an exception.</summary>
    public bool IsError { get; set; }

    /// <summary>Exception type name (null when no exception).</summary>
    public string? ExceptionType { get; set; }

    /// <summary>Exception message (null when no exception).</summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>Elapsed time in milliseconds.</summary>
    public long ElapsedMs { get; set; }
}

/// <summary>
/// Implement this interface and register it in DI to receive audit entries.
/// Without a registration, <see cref="AuditHandler"/> silently no-ops.
/// </summary>
/// <example>
/// <code>
/// public class DbAuditStore : IAuditStore
/// {
///     private readonly AppDbContext _db;
///     public DbAuditStore(AppDbContext db) => _db = db;
///     public async ValueTask WriteAsync(AuditEntry entry, CancellationToken ct)
///     {
///         _db.AuditLog.Add(entry);
///         await _db.SaveChangesAsync(ct);
///     }
/// }
///
/// // Program.cs:
/// builder.Services.AddScoped&lt;IAuditStore, DbAuditStore&gt;();
/// </code>
/// </example>
public interface IAuditStore
{
    System.Threading.Tasks.Task WriteAsync(AuditEntry entry, System.Threading.CancellationToken ct = default);
}
