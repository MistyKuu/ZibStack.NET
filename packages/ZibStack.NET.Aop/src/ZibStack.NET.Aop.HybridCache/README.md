# ZibStack.NET.Aop.HybridCache

HybridCache aspect for [ZibStack.NET.Aop](https://www.nuget.org/packages/ZibStack.NET.Aop) — L1 memory + L2 distributed caching via `Microsoft.Extensions.Caching.Hybrid`.

## Install

```
dotnet add package ZibStack.NET.Aop
dotnet add package ZibStack.NET.Aop.HybridCache
```

## Setup

```csharp
builder.Services.AddHybridCache();          // Microsoft's HybridCache
builder.Services.AddAop();                  // built-in aspects
builder.Services.AddAopHybridCache();       // [HybridCache] handler

var app = builder.Build();
app.Services.UseAop();
```

For L2 (distributed) caching, add a distributed cache provider:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
```

## Usage

```csharp
[HybridCache(DurationSeconds = 120)]
public async Task<Product> GetProductAsync(int id) { ... }

// Custom cache key:
[HybridCache(KeyTemplate = "user:{userId}:orders")]
public async Task<List<Order>> GetOrdersAsync(int userId) { ... }
```

Async methods only (`IAsyncAroundAspectHandler`). For sync in-memory caching, use the built-in `[Cache]` attribute.

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/aop/](https://mistykuu.github.io/ZibStack.NET/packages/aop/)
