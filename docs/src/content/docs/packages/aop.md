---
title: ZibStack.NET.Aop
description: AOP (Aspect-Oriented Programming) framework for .NET 8+ using C# interceptors — compile-time aspects with no runtime proxy or reflection.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Aop.svg)](https://www.nuget.org/packages/ZibStack.NET.Aop) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Aop)

AOP (Aspect-Oriented Programming) framework for .NET 8+ using **C# interceptors**. Define aspects that run before, after, or on exception of any method — at compile time, no runtime proxy or reflection.

> **See the working sample:** [SampleApi on GitHub](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Aop/sample/SampleApi)

> **Compile-time diagnostics:** every aspect placement in this guide is also validated by 15 Roslyn analyzers shipped in the same package — `AOP0001` through `AOP0021`. Bad placements (`[Cache]` on a `void` method, `[Retry(MaxAttempts = 0)]`, `[Log]` on a `private` method, …) light up in the IDE before you can build, and 7 of them have an Alt+Enter code fix. See **[AOP Analyzers — Compile-Time Diagnostics](./aop-analyzers/)** for the full reference.

## Install

```
dotnet add package ZibStack.NET.Aop
```

> The package's `build/.props` enables `InterceptorsNamespaces` for `ZibStack.Generated` automatically on restore — no manual `.csproj` edit required.

## Setup (DI)

All aspect handlers are resolved from DI. There are **two things** you must do at startup:

1. **Register every handler type** in the DI container (`AddTransient` / `AddScoped` / `AddSingleton`).
   Built-in handlers ship with a one-call helper: `AddAop()`.
2. **Bridge the container** to the aspect runtime by calling `UseAop()` after `Build()`.

```csharp
using ZibStack.NET.Aop;

var builder = WebApplication.CreateBuilder(args);

// 1a. Register built-in ZibStack aspect handlers ([Trace], ...).
builder.Services.AddAop();

// 1b. Register any of your own handlers that you reference via [AspectHandler(typeof(...))].
builder.Services.AddTransient<TimingHandler>();
builder.Services.AddSingleton<ITimingRecorder, MyMetricsRecorder>();

var app = builder.Build();

// 2. Bridge DI into the aspect runtime — one call, required once.
app.Services.UseAop();
```

Both steps are mandatory:

- **Forget step 2** → first call into any aspect-decorated method throws:
  > `InvalidOperationException: ZibStack.NET.Aop.AspectServiceProvider.ServiceProvider is not set. [Log] resolves ILogger<T> from DI; you must wire it once at app startup. For ASP.NET Core: 'var app = builder.Build(); app.Services.UseAop();'`
- **Forget step 1** (handler missing from DI) → throws:
  > `InvalidOperationException: Aspect handler 'YourHandler' is not registered in DI. Add 'builder.Services.AddTransient<YourHandler>();' at startup.`

> `UseAop()` is a thin wrapper that sets `AspectServiceProvider.ServiceProvider = services`. If you prefer the assignment form you can still use it — they are equivalent.

You'll see the same error for every handler attribute you stack on a method, so register all of them up-front.

### Dependency injection in handlers

Handlers are resolved from DI — they support **constructor injection** like any other service:

```csharp
public class TimingHandler : IAspectHandler
{
    private readonly ILogger<TimingHandler> _logger;
    private readonly ITimingRecorder _recorder;

    // Dependencies injected automatically by the DI container
    public TimingHandler(ILogger<TimingHandler> logger, ITimingRecorder recorder)
    {
        _logger = logger;
        _recorder = recorder;
    }

    public void OnBefore(AspectContext ctx) { }

    public void OnAfter(AspectContext ctx)
    {
        _logger.LogInformation("{Class}.{Method} completed in {Ms}ms",
            ctx.ClassName, ctx.MethodName, ctx.ElapsedMilliseconds);
        _recorder.Record(ctx.MethodName, ctx.ElapsedMilliseconds);
    }

    public void OnException(AspectContext ctx, Exception ex)
        => _logger.LogWarning(ex, "{Class}.{Method} failed", ctx.ClassName, ctx.MethodName);
}
```

> **Fallback:** If DI is not configured, the generator falls back to `new TimingHandler()` — which requires a parameterless constructor. To use injected dependencies, always set `AspectServiceProvider.ServiceProvider`.

## Benchmarks

Runtime handler overhead per call, measured with BenchmarkDotNet on .NET 10.0:

| Method | Mean | Allocated |
|---|---:|---:|
| Direct call (no AOP) | 0.2 ns | 0 B |
| No params (zero-alloc) | 17.4 ns | 104 B |
| **1 runtime handler** | **73.7 ns** | **360 B** |
| **2 stacked handlers** | **106.0 ns** | **672 B** |

~74ns + 360B per handler per call. For typical API endpoints (1-10ms), this is <0.01% overhead.

For hot paths, use an **inline emitter** (`[Log]` does this) — see [Inline Emitters vs Runtime Handlers](#inline-emitters-vs-runtime-handlers).


## Read more

- [Built-in aspects](/ZibStack.NET/packages/aop/built-in/) — `[Trace]`, `[Retry]`, `[Cache]`, `[Metrics]`, `[Timeout]`, `[Authorize]`, `[Validate]`, `[Transaction]` + Polly / HybridCache.
- [Custom aspects & internals](/ZibStack.NET/packages/aop/custom/) — write your own handlers (sync / around / async), class-level + multi-aspect application, `AspectContext` API, inline emitters.
- [AOP Analyzers — compile-time diagnostics](/ZibStack.NET/packages/aop-analyzers/) — the diagnostic IDs and code fixes you'll see in your IDE.

## Requirements

- **.NET 8.0** or later (uses C# interceptors)

The package's `build/.props` enables `InterceptorsNamespaces` automatically on restore — no manual `.csproj` edit needed.

## License

MIT
