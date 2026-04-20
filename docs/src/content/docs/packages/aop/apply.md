---
title: Bulk Apply
description: Apply aspects to entire namespaces, interfaces, or class hierarchies without per-method attributes — using the fluent Apply() DSL in IAopConfigurator.
---

## Overview

Instead of placing `[Cache]`, `[Retry]`, `[Trace]` on every method individually, use `b.Apply<TAspect>()` in your `IAopConfigurator` to apply aspects in bulk based on selectors:

```csharp
public sealed class AopConfig : IAopConfigurator
{
    public void Configure(IAopBuilder b)
    {
        b.Apply<CacheAttribute>(to => to
            .Implementing<IRepository>()
            .PublicMethods()
        , c => c.DurationSeconds = 120);
    }
}
```

This is equivalent to placing `[Cache(DurationSeconds = 120)]` on every public method in every class that implements `IRepository` — but without touching any of those classes.

## Selectors

All selectors are AND-combined (intersection). Chain as many as you need:

| Selector | Description |
|---|---|
| `.Namespace("X")` | Classes whose namespace starts with `X` (e.g. `MyApp.Services` matches `MyApp.Services.Orders`) |
| `.Implementing<T>()` | Classes implementing interface `T` |
| `.DerivedFrom<T>()` | Classes inheriting from `T` |
| `.ClassesWhere(c => ...)` | Filter by class metadata: `c.Name`, `c.IsAbstract`, `c.IsSealed` |
| `.MethodsWhere(m => ...)` | Filter by method metadata: `m.Name`, `m.IsAsync`, `m.IsPublic`, `m.IsStatic` |
| `.PublicMethods()` | Shortcut for `.MethodsWhere(m => m.IsPublic)` |
| `.Except<T>()` | Exclude a specific class from matching |

### Predicate expressions

`ClassesWhere` and `MethodsWhere` accept lambda expressions parsed at compile time. Supported patterns:

```csharp
// String predicates
c => c.Name.StartsWith("Order")
c => c.Name.EndsWith("Service")
c => c.Name.Contains("Payment")

// Boolean properties
m => m.IsAsync
m => m.IsPublic && !m.IsStatic

// Combined
m => m.Name.StartsWith("Get") && m.IsAsync
```

## Configuring aspect properties

The optional second lambda configures the aspect's properties — same as named arguments on the attribute:

```csharp
// Equivalent to [Retry(MaxAttempts = 5, DelayMs = 200)]
b.Apply<RetryAttribute>(to => to
    .Namespace("MyApp.Services")
    .MethodsWhere(m => m.IsAsync)
, r => { r.MaxAttempts = 5; r.DelayMs = 200; });
```

## Examples

### Log all services globally (no `[Log]` on any class)

```csharp
b.Apply<LogAttribute>(to => to
    .Namespace("MyApp.Services")
    .PublicMethods()
);
```

Works through interfaces, generics, overloads, diamond inheritance, and DI dispatch.

### Cache all repository methods

```csharp
b.Apply<CacheAttribute>(to => to
    .Implementing<IRepository>()
    .PublicMethods()
, c => c.DurationSeconds = 300);
```

### Retry all async service methods

```csharp
b.Apply<RetryAttribute>(to => to
    .Namespace("MyApp.Services")
    .MethodsWhere(m => m.IsAsync)
, r => r.MaxAttempts = 3);
```

### Trace everything except health checks

```csharp
b.Apply<TraceAttribute>(to => to
    .DerivedFrom<BaseController>()
    .Except<HealthCheckController>()
);
```

### Metrics on all "Order" classes

```csharp
b.Apply<MetricsAttribute>(to => to
    .ClassesWhere(c => c.Name.StartsWith("Order"))
);
```

### Timeout on all public async methods in the project

```csharp
b.Apply<TimeoutAttribute>(to => to
    .MethodsWhere(m => m.IsAsync && m.IsPublic)
, t => t.TimeoutMs = 5000);
```

## Interface dispatch (DI scenarios)

Apply rules match **classes**, not interfaces. But calls through interfaces are intercepted automatically:

```csharp
// DI registration
builder.Services.AddTransient<IOrderService, OrderService>();

// Apply rule — matches OrderService (the class)
b.Apply<LogAttribute>(to => to.Namespace("MyApp.Services").PublicMethods());

// Call site through interface — intercepted ✓
app.MapGet("/order/{id}", (int id, IOrderService svc) => svc.GetOrder(id));
```

The generator sees that `OrderService` implements `IOrderService` and automatically generates an interface proxy interceptor. No extra configuration needed.

This works with:
- **Simple interfaces** — `IOrderService svc`
- **Generic interfaces** — `IRepo<Product> svc`
- **Multiple implementations** of the same interface (first impl wins for the proxy)
- **Diamond inheritance** — `IVersioned : INamed`, `ITaggable : INamed`
- **Method overloads** — `Execute()`, `Execute(int)`, `Execute(string, int)`

### Explicit attribute on interface

You can also place aspects directly on an interface:

```csharp
[Trace]
public interface IOrderService
{
    Order GetOrder(int id);
}
```

This intercepts all calls through `IOrderService` regardless of which class implements it. If you ALSO have an Apply rule matching the impl class — deduplication kicks in, no conflict.

## Priority

1. **Explicit `[Attribute]`** on a method/class/interface always wins — Apply rules don't override existing attributes
2. **Apply rules** add virtual aspects to methods that don't already have them
3. **Project-wide defaults** (`b.Retry(...)`, `b.Cache(...)`) fill in unset properties on both explicit attributes and Apply-applied aspects
4. **Deduplication** — if the same interface gets a model from both explicit attribute and Apply proxy, only one is kept (no duplicate interceptors)

## How it works

The generator parses the `Configure` method body at compile time — it is **never invoked at runtime**. Selector chains and predicate lambdas are evaluated against Roslyn symbols during source generation. Matched methods receive interceptors identical to those generated for explicit attributes.

For interface dispatch, the generator:
1. Finds all classes matching the Apply rule
2. For each class, discovers all source-declared interfaces it implements
3. Generates an interface proxy (extension method on `this IMyInterface`) so call sites through the interface hit the interceptor

## Advanced scenarios

### Generic interfaces with multiple type arguments

```csharp
public interface ICommandHandler<TCommand, TResult>
{
    TResult Handle(TCommand command);
}

public class CreateOrderHandler : ICommandHandler<CreateOrder, OrderResult> { ... }
public class CancelOrderHandler : ICommandHandler<CancelOrder, string> { ... }

// Both handlers intercepted — each closed generic gets its own proxy
b.Apply<TraceAttribute>(to => to.Implementing<ICommandHandler<,>>().PublicMethods());
// or simply:
b.Apply<TraceAttribute>(to => to.Namespace("MyApp.Handlers").PublicMethods());
```

### Same interface implemented by many classes

```csharp
public interface ITransformFunction
{
    JsonNode Execute(JsonNode[] args);
}

public class ConcatFunction : ITransformFunction { ... }
public class UpperFunction : ITransformFunction { ... }
public class TrimFunction : ITransformFunction { ... }
// ... 20 more

// One rule instruments all of them — first impl generates the interface proxy
b.Apply<LogAttribute>(to => to.Implementing<ITransformFunction>().PublicMethods());
```

### Composed interface (CQRS pattern)

```csharp
public interface ICanHandle<TCommand> { string Handle(TCommand cmd); }
public interface ICanHandle<TCommand, TResult> { TResult Handle(TCommand cmd); }

public interface IOrderHandler
    : ICanHandle<CreateOrder, OrderResult>,
      ICanHandle<CancelOrder>
{
    int GetPendingCount();
}

public class OrderHandler : IOrderHandler { ... }

// All of these call sites are intercepted:
IOrderHandler svc = ...;
svc.GetPendingCount();                              // own method
svc.Handle(new CreateOrder(...));                   // from ICanHandle<CreateOrder, OrderResult>

ICanHandle<CancelOrder> cancel = svc;
cancel.Handle(new CancelOrder(42));                 // from ICanHandle<CancelOrder>
```

### Mixed: concrete + interface call on same object

```csharp
var impl = new OrderService();
IOrderService iface = impl;

impl.GetOrder(1);    // intercepted via OrderService_Aop (concrete)
iface.GetOrder(2);   // intercepted via IOrderService_IfaceAop (proxy)
// Both work, no conflict — different interceptor classes
```

### Excluding specific classes

```csharp
b.Apply<LogAttribute>(to => to
    .Namespace("MyApp.Services")
    .PublicMethods()
    .Except<HealthCheckService>()    // too noisy
    .Except<MetricsService>()        // avoid recursion
);
```

### Combining multiple Apply rules

```csharp
public sealed class AopConfig : IAopConfigurator
{
    public void Configure(IAopBuilder b)
    {
        // Observability on everything
        b.Apply<TraceAttribute>(to => to.Namespace("MyApp").PublicMethods());
        b.Apply<LogAttribute>(to => to.Namespace("MyApp").PublicMethods());

        // Retry only on external calls
        b.Apply<RetryAttribute>(to => to
            .Namespace("MyApp.External")
            .MethodsWhere(m => m.IsAsync)
        , r => r.MaxAttempts = 3);

        // Cache on read-only repos
        b.Apply<CacheAttribute>(to => to
            .Implementing<IReadOnlyRepository>()
            .MethodsWhere(m => m.Name.StartsWith("Get"))
        , c => c.DurationSeconds = 60);
    }
}
```

A method can receive aspects from multiple rules — they stack in declaration order.
