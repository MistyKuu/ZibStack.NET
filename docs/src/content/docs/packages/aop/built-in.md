---
title: Built-in aspects
description: Reference for the aspects that ship in ZibStack.NET.Aop — Trace, Retry, Cache, Metrics, Timeout, Authorize, Validate, Transaction, Audit, plus the Polly + HybridCache add-ons.
---

## Built-in Aspects

`AddAop()` registers all built-in aspect handlers. No extra DI registration needed — just apply the attribute.

| Attribute | Handler | Description |
|---|---|---|
| `[Trace]` | `TraceHandler` | OpenTelemetry `Activity` spans with parameter tags, timing, status |
| `[Retry]` | `RetryHandler` | Retry with backoff, exception filtering (`Handle`/`Ignore`) |
| `[Cache]` | `CacheHandler` | In-memory cache with TTL and `KeyTemplate` support |
| `[Metrics]` | `MetricsHandler` | `System.Diagnostics.Metrics` counters + duration histogram |
| `[Timeout]` | `TimeoutHandler` | Async execution time limit, throws `TimeoutException` |
| `[Authorize]` | `AuthorizeHandler` | Policy/role-based authorization via `IAuthorizationProvider` |
| `[Validate]` | `ValidateHandler` | Auto-validate parameters via `DataAnnotations` before execution |
| `[Transaction]` | `TransactionHandler` | Wrap method in `TransactionScope` (commit/rollback) |
| `[Audit]` | `AuditHandler` | Before/after parameter snapshots → `IAuditStore`; respects `[Sensitive]`/`[NoLog]` |

Optional (separate packages):

| Attribute | Package | Description |
|---|---|---|
| `[PollyRetry]` | `ZibStack.NET.Aop.Polly` | Polly retry — named pipelines, backoff strategies, exception filtering |
| `[PollyHttpRetry]` | `ZibStack.NET.Aop.Polly` | Transient HTTP error retry (408/429/5xx, `HttpRequestException`, timeouts) |
| `[PollyCircuitBreaker]` | `ZibStack.NET.Aop.Polly` | Circuit breaker — trips after failure threshold, fast-fails, half-open probe |
| `[PollyRateLimiter]` | `ZibStack.NET.Aop.Polly` | Fixed window rate limiter — rejects excess calls |
| `[HybridCache]` | `ZibStack.NET.Aop.HybridCache` | L1/L2 caching via `Microsoft.Extensions.Caching.Hybrid` |

### `[Trace]` — OpenTelemetry spans

`[Trace]` wraps the decorated method in a `System.Diagnostics.Activity` span, compatible with **OpenTelemetry, Jaeger, Zipkin, Application Insights, and any OTLP exporter**. It ships with `ZibStack.NET.Aop.Abstractions` — no extra package, no handwritten instrumentation.

```csharp
using ZibStack.NET.Aop;

[Trace]
public async Task<Order> GetOrderAsync(int id)
{
    // whatever — the span opens on entry and closes on exit
    return await _repo.FindAsync(id);
}
```

What the handler does automatically:

- Creates (or reuses, thread-safely) an `ActivitySource` per declaring class.
- Starts an `Activity` on entry with kind `Internal`.
- Attaches method parameters as span tags — honoring `[Sensitive]` (masked as `***`) and `[NoLog]` (excluded).
- Adds `code.namespace`, `code.function`, `elapsed_ms` tags following OpenTelemetry semantic conventions.
- Sets `ActivityStatusCode.Ok` on success, `ActivityStatusCode.Error` + exception tags on failure.
- Disposes the activity in both happy and exception paths.

### Options

```csharp
// Group spans under a logical service name instead of the class name:
[Trace(SourceName = "checkout.orders")]
public Task PlaceOrderAsync(Order order) { ... }

// Override the span / operation name:
[Trace(OperationName = "orders.place")]
public Task PlaceOrderAsync(Order order) { ... }

// Skip parameter tags on wide/hot signatures:
[Trace(IncludeParameters = false)]
public IEnumerable<Row> ScanAll(LargeFilter filter) { ... }

// Apply to every public method on a class:
[Trace]
public class OrderService { /* all public methods are traced */ }
```

### Exporter wiring

`[Trace]` only produces `Activity` objects — you still need an OpenTelemetry exporter to see them. A typical setup:

```csharp
builder.Services.AddAop();  // registers TraceHandler

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("*")               // listen to all ZibStack-generated sources (one per class)
        .AddOtlpExporter());          // or AddJaegerExporter / AddZipkinExporter / ...
```

If you used `SourceName = "..."` overrides, add them explicitly instead of `"*"`.

### `[Retry]` — automatic retry with backoff

```csharp
// Simple retry — 3 attempts, no delay:
[Retry]
public string FetchData(string url) { ... }

// With exponential backoff:
[Retry(MaxAttempts = 3, DelayMs = 200, BackoffMultiplier = 2.0)]
public async Task<Order> GetOrderFromApiAsync(int id) { ... }

// Exception filtering — opt-in (only retry these):
[Retry(MaxAttempts = 3, Handle = new[] { typeof(HttpRequestException), typeof(TimeoutException) })]
public string CallExternalService() { ... }

// Exception filtering — opt-out (retry everything except these):
[Retry(MaxAttempts = 3, Ignore = new[] { typeof(ArgumentException) })]
public void ProcessRequest(Request req) { ... }
```

Properties: `MaxAttempts` (default 3, includes initial call), `DelayMs` (default 0), `BackoffMultiplier` (default 1.0), `Handle` (`Type[]`, opt-in filter), `Ignore` (`Type[]`, opt-out filter).

Exception matching uses `IsAssignableFrom` — `Handle = new[] { typeof(IOException) }` catches `HttpIOException` too.

Works on both sync and async methods (`IAroundAspectHandler` + `IAsyncAroundAspectHandler`).

### `[Cache]` — in-memory caching

```csharp
// Default: 5 min TTL, key from class + method + parameters
[Cache]
public Product GetProduct(int id) { ... }

// Custom TTL:
[Cache(DurationSeconds = 60)]
public List<Country> GetCountries() { ... }

// Custom cache key with parameter placeholders (nested properties supported):
[Cache(KeyTemplate = "product:{id}")]
public Product GetProduct(int id, bool includeArchived) { ... }

[Cache(KeyTemplate = "order:{order.Customer.Id}:{order.Status}")]
public Invoice GetInvoice(Order order) { ... }

// Infinite cache (until manual invalidation):
[Cache(DurationSeconds = 0)]
public IReadOnlyList<Currency> GetCurrencies() { ... }
```

Manual invalidation:

```csharp
CacheHandler.Invalidate("GetProduct");   // clears all entries containing "GetProduct"
CacheHandler.ClearAll();                  // clears everything
```

**`KeyTemplate`** placeholders are expanded at compile time into C# interpolated strings — the generator emits `$"product:{id}"` directly. Invalid parameter references produce compiler errors.

### `[Metrics]` — System.Diagnostics.Metrics

Records call count, duration, and errors using the standard .NET metrics API. Three instruments, shared across all decorated methods, differentiated by tags:

```
Meter: "ZibStack.Aop"
├── aop.method.call.count     (Counter<long>)
├── aop.method.call.duration  (Histogram<double>, ms)
└── aop.method.call.errors    (Counter<long>)
```

Tags per measurement: `aop.class`, `aop.method`, and optionally `aop.metric` (from `MetricName`).

```csharp
[Metrics]
public Order GetOrder(int id) { ... }

// Custom grouping tag:
[Metrics(MetricName = "checkout.orders")]
public Order PlaceOrder(OrderRequest req) { ... }
```

Exporter wiring:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("ZibStack.Aop"));
```

Or `dotnet-counters monitor --counters ZibStack.Aop`.

When `IMeterFactory` is available in DI (standard in ASP.NET Core 8+), the handler uses it. Otherwise falls back to `new Meter(...)`.

### `[Timeout]` — async execution time limit

```csharp
[Timeout(TimeoutMs = 5000)]
public async Task<Report> GenerateReportAsync(int id) { ... }
```

Throws `TimeoutException` if the method doesn't complete in time. **Async methods only** (`IAsyncAroundAspectHandler`).

### `[Authorize]` — policy/role-based authorization

```csharp
// Role-based:
[Authorize(Roles = "Admin,Manager")]
public async Task DeleteOrderAsync(int id) { ... }

// Policy-based:
[Authorize(Policy = "CanEditProducts")]
public async Task<Product> UpdateProductAsync(int id, ProductDto dto) { ... }

// Authentication only (no specific role/policy):
[Authorize]
public async Task<UserProfile> GetProfileAsync() { ... }
```

Requires an `IAuthorizationProvider` implementation registered in DI:

```csharp
builder.Services.AddSingleton<IAuthorizationProvider, MyAuthProvider>();
```

```csharp
public class MyAuthProvider : IAuthorizationProvider
{
    private readonly IHttpContextAccessor _accessor;
    public MyAuthProvider(IHttpContextAccessor accessor) => _accessor = accessor;

    public ValueTask<bool> IsAuthorizedAsync(string policy)
    {
        var user = _accessor.HttpContext?.User;
        if (policy == "__authenticated") return new(user?.Identity?.IsAuthenticated == true);
        return new(user?.HasClaim("policy", policy) == true);
    }

    public ValueTask<bool> IsInRoleAsync(string role)
        => new(_accessor.HttpContext?.User?.IsInRole(role) == true);
}
```

Throws `UnauthorizedAccessException` on failure. **Async methods only** (`IAsyncAspectHandler`).

### `[PollyRetry]` — Polly integration (optional)

Requires `Polly.Core` NuGet package. Two modes:

```csharp
// Inline — builds pipeline from attribute properties:
[PollyRetry(MaxRetryAttempts = 3, DelayMs = 200, BackoffType = RetryBackoffType.Exponential)]
public async Task<Order> GetOrderAsync(int id) { ... }

// Named pipeline — references a pre-configured Polly pipeline from DI:
[PollyRetry(PipelineName = "external-api")]
public async Task<string> CallExternalAsync(string url) { ... }

// Exception filtering (same Type[] API as [Retry]):
[PollyRetry(Handle = new[] { typeof(HttpRequestException) })]
public async Task<string> FetchAsync() { ... }
```

Named pipeline setup:

```csharp
builder.Services.AddResiliencePipeline("external-api", builder =>
{
    builder.AddRetry(new() { MaxRetryAttempts = 5, Delay = TimeSpan.FromSeconds(1) });
    builder.AddCircuitBreaker(new() { FailureRatio = 0.5 });
    builder.AddTimeout(TimeSpan.FromSeconds(30));
});
```

### `[HybridCache]` — distributed caching (optional)

Requires `Microsoft.Extensions.Caching.Hybrid` NuGet package. L1 memory + L2 distributed (Redis, etc.):

```csharp
[HybridCache(DurationSeconds = 120)]
public async Task<Product> GetProductAsync(int id) { ... }

[HybridCache(KeyTemplate = "user:{userId}:orders")]
public async Task<List<Order>> GetOrdersAsync(int userId) { ... }
```

Setup: `builder.Services.AddHybridCache();`

**Async methods only** (`IAsyncAroundAspectHandler`).

### `[Validate]` — parameter validation

```csharp
[Validate]
public Order CreateOrder(CreateOrderRequest request) { ... }
// If request has [Required] Name = null → throws ArgumentException before method runs
```

Validates complex object parameters using `System.ComponentModel.DataAnnotations.Validator`. Primitives/strings are skipped. Works on sync and async methods.

### `[Transaction]` — TransactionScope

```csharp
[Transaction]
public void TransferFunds(int from, int to, decimal amount) { ... }

[Transaction(IsolationLevel = IsolationLevel.ReadCommitted, TimeoutSeconds = 30)]
public async Task<Order> PlaceOrderAsync(OrderRequest req) { ... }
```

Wraps method in `TransactionScope` with `TransactionScopeAsyncFlowOption.Enabled`. Commits on success, rolls back on exception. Properties: `IsolationLevel` (default `ReadCommitted`), `TimeoutSeconds` (default 30).

### `[PollyCircuitBreaker]` — circuit breaker (optional)

```csharp
[PollyCircuitBreaker(FailureThreshold = 0.5, SamplingDurationSeconds = 30, BreakDurationSeconds = 15)]
public async Task<string> CallExternalApiAsync() { ... }
```

After 50% failure rate (min 10 calls in 30s window), circuit opens — subsequent calls throw `BrokenCircuitException` for 15s. Then half-opens to probe. Each decorated method gets its own circuit.

### `[PollyRateLimiter]` — rate limiting (optional)

```csharp
[PollyRateLimiter(PermitLimit = 100, WindowSeconds = 60)]
public async Task<SearchResult> SearchAsync(string query) { ... }

[PollyRateLimiter(PermitLimit = 5, WindowSeconds = 1)]
public async Task SendNotificationAsync(int userId) { ... }
```

Fixed window rate limiter. Excess calls throw `RateLimiterRejectedException`. `QueueLimit` (default 0) controls how many excess calls queue instead of rejecting immediately.

### `[Audit]` — audit trail

`[Audit]` captures a before/after snapshot of method parameters and writes an audit record to `IAuditStore`. Useful for compliance, change tracking, and admin dashboards.

```csharp
[Audit]
public async Task<Order> PlaceOrderAsync(OrderRequest request) { ... }

// Custom action name (default: method name):
[Audit(Action = "PlaceOrder")]
public async Task<Order> PlaceOrderAsync(OrderRequest request) { ... }
```

What the handler does:

- Snapshots all parameters **before** method execution (serialized to JSON).
- Snapshots the return value and any `ref`/`out` parameters **after** execution.
- Writes an `AuditEntry` to the registered `IAuditStore` with: action name, user identity, timestamp, before/after payloads, and success/failure status.
- Respects `[Sensitive]` (value masked as `"***"`) and `[NoLog]` (parameter excluded entirely) — same attributes used by `[Trace]`.

```csharp
[Audit]
public async Task TransferFundsAsync(
    int fromAccount,
    int toAccount,
    decimal amount,
    [Sensitive] string authToken)   // stored as "***"
{ ... }
```

#### `IAuditStore`

Register an implementation in DI to control where audit records are persisted:

```csharp
public interface IAuditStore
{
    ValueTask WriteAsync(AuditEntry entry, CancellationToken ct = default);
}

public record AuditEntry(
    string Action,
    string? UserId,
    DateTimeOffset Timestamp,
    string? BeforeJson,
    string? AfterJson,
    bool Success,
    string? ErrorMessage);
```

Example EF Core implementation:

```csharp
public class DbAuditStore : IAuditStore
{
    private readonly AppDbContext _db;
    public DbAuditStore(AppDbContext db) => _db = db;

    public async ValueTask WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        _db.AuditLog.Add(new AuditLogRow
        {
            Action    = entry.Action,
            UserId    = entry.UserId,
            Timestamp = entry.Timestamp,
            Before    = entry.BeforeJson,
            After     = entry.AfterJson,
            Success   = entry.Success,
            Error     = entry.ErrorMessage,
        });
        await _db.SaveChangesAsync(ct);
    }
}
```

Registration:

```csharp
builder.Services.AddAop();
builder.Services.AddScoped<IAuditStore, DbAuditStore>();
```

Works on both sync and async methods. The user identity is resolved from `IHttpContextAccessor` when available, otherwise from `Thread.CurrentPrincipal`.

### When to use `[Trace]` vs manual `using var activity = ...`

Use `[Trace]` when you want:
- **Declarative** span boundaries — no `try`/`catch`/`Dispose` boilerplate in every method.
- **Uniform tags** — parameters, timing, class/method names attached consistently.
- **Composability** — stack `[Log]`, `[Trace]`, `[Timing]` without leaking observability code into business logic.

Write manual activities when you need to attach dynamic tags that depend on mid-method state the handler can't see, or when you're creating child spans inside a single method.

