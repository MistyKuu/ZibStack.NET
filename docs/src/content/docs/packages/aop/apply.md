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

## Priority

1. **Explicit `[Attribute]`** on a method always wins — Apply rules don't override existing attributes
2. **Apply rules** add virtual aspects to methods that don't already have them
3. **Project-wide defaults** (`b.Retry(...)`, `b.Cache(...)`) fill in unset properties on both explicit attributes and Apply-applied aspects

## How it works

The generator parses the `Configure` method body at compile time — it is **never invoked at runtime**. Selector chains and predicate lambdas are evaluated against Roslyn symbols during source generation. Matched methods receive interceptors identical to those generated for explicit attributes.
