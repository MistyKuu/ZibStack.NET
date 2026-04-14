# ZibStack.NET.Aop.Polly

Polly-based resilience aspects for [ZibStack.NET.Aop](https://www.nuget.org/packages/ZibStack.NET.Aop).

## Install

```
dotnet add package ZibStack.NET.Aop
dotnet add package ZibStack.NET.Aop.Polly
```

## Setup

```csharp
builder.Services.AddAop();        // built-in aspects ([Trace], [Retry], [Cache], ...)
builder.Services.AddAopPolly();   // [PollyRetry], [PollyHttpRetry], [PollyCircuitBreaker], [PollyRateLimiter]

var app = builder.Build();
app.Services.UseAop();
```

## `[PollyHttpRetry]` — transient HTTP error retry

```csharp
[PollyHttpRetry]
public async Task<string> CallApiAsync(string url) { ... }

[PollyHttpRetry(MaxRetryAttempts = 5, DelayMs = 500)]
public async Task<OrderResponse> PlaceOrderAsync(OrderRequest req) { ... }
```

Handles `HttpRequestException` (transient status codes: 408, 429, 5xx), `TaskCanceledException` (timeouts). Exponential backoff with jitter.

## `[PollyRetry]` — full Polly retry

```csharp
// Inline pipeline:
[PollyRetry(MaxRetryAttempts = 3, DelayMs = 200, BackoffType = RetryBackoffType.Exponential)]
public async Task<Order> GetOrderAsync(int id) { ... }

// Exception filtering:
[PollyRetry(Handle = new[] { typeof(HttpRequestException) })]
public async Task<string> FetchAsync() { ... }

// Named pipeline (requires Microsoft.Extensions.Resilience):
[PollyRetry(PipelineName = "external-api")]
public async Task<string> CallExternalAsync(string url) { ... }
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

## `[PollyCircuitBreaker]` — circuit breaker

```csharp
[PollyCircuitBreaker(FailureThreshold = 0.5, SamplingDurationSeconds = 30, BreakDurationSeconds = 15)]
public async Task<string> CallExternalApiAsync() { ... }
```

Trips after failure threshold, fast-fails with `BrokenCircuitException`, half-opens after break duration.

## `[PollyRateLimiter]` — rate limiting

```csharp
[PollyRateLimiter(PermitLimit = 100, WindowSeconds = 60)]
public async Task<SearchResult> SearchAsync(string query) { ... }
```

Fixed window rate limiter. Excess calls throw `RateLimiterRejectedException`.

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/aop/](https://mistykuu.github.io/ZibStack.NET/packages/aop/)
