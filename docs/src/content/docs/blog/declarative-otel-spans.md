---
title: "I replaced 800 lines of OpenTelemetry boilerplate with one attribute"
description: "How a Roslyn source generator turns [Trace] into a full Activity span — no try/catch ceremony, no copy-paste across 40 methods."
---

You know that moment when you realize you've been copy-pasting the same 20 lines into every service method for the past hour? That was me last year, instrumenting a microservice with OpenTelemetry. Every. Single. Method. Looked like this:

```csharp
public async Task<Order> PlaceOrderAsync(int customerId, string product, int quantity)
{
    using var activity = _activitySource.StartActivity("PlaceOrderAsync");
    activity?.SetTag("customerId", customerId);
    activity?.SetTag("product", product);
    activity?.SetTag("quantity", quantity);
    try
    {
        var order = await _repo.CreateAsync(customerId, product, quantity);
        activity?.SetTag("orderId", order.Id);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return order;
    }
    catch (Exception ex)
    {
        activity?.SetTag("exception.type", ex.GetType().FullName);
        activity?.SetTag("exception.message", ex.Message);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

That's ~20 lines of noise for a single span. And you end up copy-pasting it into every service method. I had about 40 of these in one project. 800 lines of try/catch/SetTag/Dispose, all slightly different, all doing the same thing.

So I built this:

```csharp
[Trace]
public async Task<Order> PlaceOrderAsync(int customerId, string product, int quantity)
{
    return await _repo.CreateAsync(customerId, product, quantity);
}
```

Opens the same span in Jaeger. Tags the parameters. Records errors. Done.

## OK but what's it doing under the hood

So `[Trace]` comes from a library I've been building called [ZibStack.NET.Aop](https://github.com/MistyKuu/ZibStack.NET). The trick is C# 12 interceptors — relatively new Roslyn feature that lets a source generator rewrite a specific call site at compile time. Not IL weaving like PostSharp used to do. Not runtime proxies like Castle.DynamicProxy. Actual C# code the compiler generates and you can go read in `obj/` if you don't trust it.

When the generator spots `[Trace]` on your method, it emits an interceptor that grabs the `TraceHandler` from DI, stuffs the method name and parameter values into an `AspectContext`, calls `OnBefore` (that's where `Activity.Start` happens), runs your original method body, then calls `OnAfter` or `OnException`. The whole thing is a static method — no reflection, your methods don't need to be `virtual`, no interface wrapper needed.

## Setup (it's three lines, I promise)

Wire up AOP in your `Program.cs` — this registers the built-in handlers including `TraceHandler`:

```csharp
builder.Services.AddAop();
var app = builder.Build();
app.Services.UseAop();
```

Then plug in whatever OTel exporter you're already using. I run Jaeger locally via Docker, but OTLP/Zipkin/App Insights all work:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("*").AddOtlpExporter());
```

That `"*"` wildcard catches every ActivitySource in the process. ZibStack names them after the class by default (`"MyApp.OrderService"` etc). If you want something custom — `[Trace(SourceName = "checkout.orders")]` overrides it per method or per class.

## The Jaeger output

Calling `PlaceOrderAsync(42, "Widget", 3)` produces a span with:

- Name: `PlaceOrderAsync`  
- Tags: `code.namespace=OrderService`, `code.function=PlaceOrderAsync`, `customerId=42`, `product=Widget`, `quantity=3`, `elapsed_ms=12`
- Status: `Ok` or `Error` with exception type and message

<!-- TODO: Jaeger screenshot -->

## Sensitive parameters

This bit was important to me. You're tracing method parameters, great — but then someone passes a credit card:

```csharp
[Trace]
public async Task<Receipt> ChargeAsync(
    int orderId,
    [Sensitive] string creditCard,
    [NoLog] byte[] internalToken)
{
    // ...
}
```

`creditCard` becomes `***` in the span tags. `internalToken` is skipped entirely. Same two attributes also work with `[Log]` for structured logging — you tag a parameter once and it's masked across the board.

## Throwing it on a whole class

Most of my services ended up with `[Trace]` on every method anyway:

```csharp
[Trace]
public class OrderService
{
    public async Task<Order> GetAsync(int id) { ... }
    public async Task<Order> CreateAsync(CreateRequest req) { ... }
    public async Task DeleteAsync(int id) { ... }
}
```

Put it on the class, every public method gets a span. That's the setup I use in most projects now.

## Stacking multiple concerns

The thing I didn't expect to work so cleanly — you can pile attributes:

```csharp
[Trace]
[Metrics]
[Retry(MaxAttempts = 3, DelayMs = 200)]
[Log]
public async Task<Order> PlaceOrderAsync(int customerId, string product, int quantity)
{
    return await _repo.CreateAsync(customerId, product, quantity);
}
```

Tracing, Prometheus metrics, retry on transient failure, structured entry/exit logging. Four concerns. The generator chains them into one interceptor — single try/catch, no nested wrapping. I was honestly surprised it composed that well.

## "But middleware already traces my requests"

Sure. The OTel ASP.NET Core instrumentation gives you one span per HTTP request. That tells you `/api/orders POST` took 450ms. Cool. But which method inside that request was slow? Was it the DB call? The payment gateway? The email sender?

`[Trace]` gives you spans *inside* the request pipeline. Middleware sees the HTTP boundary. This sees the code. They're complementary — you want both.

## Repo & install

```bash
dotnet add package ZibStack.NET.Aop
```

Everything's MIT licensed, targets .NET 8 and up. If you want to see the full Jaeger setup end-to-end (docker-compose, exporter config, the works), there's an [observability guide](https://mistykuu.github.io/ZibStack.NET/guides/observability/) on the docs site. The source is at [github.com/MistyKuu/ZibStack.NET](https://github.com/MistyKuu/ZibStack.NET) — issues and PRs welcome, I'm one person building this so feedback genuinely helps.
