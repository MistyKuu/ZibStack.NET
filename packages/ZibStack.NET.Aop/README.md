# ZibStack.NET.Aop

AOP framework for .NET 8+ using **C# interceptors** — define aspects that run before, after, or around any method at compile time. No runtime proxy, no reflection.

## Install

```
dotnet add package ZibStack.NET.Aop
```

## Built-in: `[Trace]` — OpenTelemetry spans, one attribute

```csharp
using ZibStack.NET.Aop;

[Trace]
public async Task<Order> GetOrderAsync(int id) => await _repo.FindAsync(id);
```

Automatic `Activity` span per call, parameters attached as tags (honoring `[Sensitive]` / `[NoLog]`), `elapsed_ms`, status, and exception tags. Works with any OpenTelemetry / Jaeger / Zipkin / OTLP exporter — see the [docs](https://mistykuu.github.io/ZibStack.NET/packages/aop/#built-in-trace--opentelemetry-spans-one-attribute) for options.

## Setup

```csharp
using ZibStack.NET.Aop;

builder.Services.AddAop();      // registers built-in handlers ([Trace], ...)
builder.Services.AddTransient<MyHandler>();  // your own handlers

var app = builder.Build();
app.Services.UseAop();                   // bridges DI into the aspect runtime — required
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

Also supported: `IAroundAspectHandler<T>` (strongly-typed full control), `IAsyncAspectHandler`, class-level aspects, multi-aspect stacking with `Order`.

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/aop/](https://mistykuu.github.io/ZibStack.NET/packages/aop/)
