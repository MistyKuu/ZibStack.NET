# ZibStack.NET.Aop

AOP framework for .NET 8+ using **C# interceptors** — define aspects that run before, after, or around any method at compile time. No runtime proxy, no reflection.

## Install

```
dotnet add package ZibStack.NET.Aop
```

## Built-in Aspects

All registered automatically by `AddAop()`:

| Attribute | What it does |
|---|---|
| `[Trace]` | OpenTelemetry `Activity` spans with parameter tags, timing, status |
| `[Retry]` | Retry with backoff + exception filtering (`Handle`/`Ignore` as `Type[]`) |
| `[Cache]` | In-memory cache with TTL and compile-time `KeyTemplate` |
| `[Metrics]` | `System.Diagnostics.Metrics` — call count, duration histogram, error count |
| `[Timeout]` | Async execution time limit, throws `TimeoutException` |
| `[Authorize]` | Policy/role-based auth via `IAuthorizationProvider` |
| `[Debounce]` | Quiet-period delay — rapid calls collapse into a single execution |
| `[Throttle]` | Rate limiting — at most one call per interval, optional trailing fire |

Optional (require external packages): `[PollyRetry]` (Polly.Core), `[HybridCache]` (Microsoft.Extensions.Caching.Hybrid).

```csharp
// One attribute per concern:
[Trace]
[Retry(MaxAttempts = 3, Handle = new[] { typeof(HttpRequestException) })]
[Cache(KeyTemplate = "order:{id}", DurationSeconds = 60)]
[Metrics]
public async Task<Order> GetOrderAsync(int id) { ... }
```

## Setup

```csharp
using ZibStack.NET.Aop;

builder.Services.AddAop();                       // registers all built-in handlers
builder.Services.AddTransient<MyHandler>();       // your own handlers

var app = builder.Build();
app.Services.UseAop();                           // bridges DI into the aspect runtime — required
```

## Custom aspects

```csharp
[AspectHandler(typeof(TimingHandler))]
public class TimingAttribute : AspectAttribute { }

public class TimingHandler : IAspectHandler
{
    public void OnBefore(AspectContext ctx)
        => Console.WriteLine($"Before {ctx.MethodName}");
    public void OnAfter(AspectContext ctx)
        => Console.WriteLine($"After {ctx.MethodName} in {ctx.ElapsedMilliseconds}ms");
    public void OnException(AspectContext ctx, Exception ex)
        => Console.WriteLine($"Error in {ctx.MethodName}: {ex.Message}");
}

// Apply anywhere:
[Timing]
public Order GetOrder(int id) { ... }
```

Also supported: `IAroundAspectHandler<T>` (strongly-typed), `IAsyncAspectHandler`, `IAsyncAroundAspectHandler`, dual sync+async interfaces, class-level aspects, multi-aspect stacking with `Order`.

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/aop/](https://mistykuu.github.io/ZibStack.NET/packages/aop/)
