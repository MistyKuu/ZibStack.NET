---
title: AOP Analyzers — Compile-Time Diagnostics
description: Compile-time error and warning diagnostics for ZibStack.NET.Aop, plus Roslyn code fixes. Catches dead-end aspect placements (static, ref, private), invalid attribute arguments (Retry MaxAttempts, Timeout TimeoutMs), and bypassed call sites (delegate conversion, base.) — directly in the IDE, before the build.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Aop.svg)](https://www.nuget.org/packages/ZibStack.NET.Aop) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Aop/src/ZibStack.NET.Aop.Analyzers)

When you install `ZibStack.NET.Aop` you also get a set of **Roslyn analyzers and code fixes** that catch broken aspect placements *and enforce architectural rules* at compile time. The diagnostics show up directly in your IDE — red squiggle, light-bulb fix, no waiting for a build. No separate package install required.

> **Why this matters:** the source-generator part can only emit interceptors for placements that are physically possible. Anything else (aspect on a `static` method, `[Cache]` on a `void` method, `[Retry(MaxAttempts = 0)]`) silently no-ops at runtime and you'd never know. Analyzers turn those silent failures into immediate, locatable errors.

## Diagnostic Categories

Four families, all under the `ZibStack.Aop` category:

| Family | Covers | Severity |
|---|---|---|
| **Tier 1 — Placement** (`AOP0001`–`AOP0006`) | The mechanics of C# interceptors: where an aspect can be placed and what kind of method it can wrap. | Mostly Error |
| **Tier 2 — Attribute Arguments** (`AOP0010`–`AOP0017`) | Per-aspect semantic checks of the values you pass. Built-in aspects only (`[Cache]`, `[Retry]`, `[Timeout]`, `[Validate]`). | Error / Warning / Info |
| **Tier 3 — Call Sites** (`AOP0020`–`AOP0021`) | Code patterns that *look* like they would invoke the aspect but actually bypass the interceptor — or, in the case of `base.Method()` over an aspect-decorated virtual, recurse infinitely. | Warning / Error |
| **Tier 4 — Convention Enforcement** (`AOP1001`–`AOP1003`) | Architectural rules you declare on a base type, enforced on every concrete derivative — required aspects, required interface implementations, required methods. | Warning |

## Tier 1 — Placement

These fire on the method (or attribute) that the aspect is applied to.

### `AOP0001` — Aspect on static method (Error)

C# interceptors require an instance receiver (`this @this`). Static methods cannot be intercepted.

```csharp
public class Svc
{
    [Log]
    public static void DoWork() { }   // ❌ AOP0001
}
```

**Code fix:** Remove the aspect attribute.

### `AOP0002` — Aspect on private/protected method (Error)

The generated interceptor lives in a separate `__X_Aop` class and is not a member of the target type nor a derived class. C# access rules forbid it from invoking `private`/`protected`/`private protected` methods.

```csharp
public class Svc
{
    [Log]
    private void DoWork() { }   // ❌ AOP0002
}
```

**Code fix:** Make the method `internal` (lowest accessibility the interceptor can reach).

> Class-level aspects automatically pick up `public`, `internal`, and `protected internal` instance methods of the type — no warning fires for those, since the parser simply skips members it can't intercept.

### `AOP0003` — Aspect on method with ref/out/in parameter (Error)

The interceptor stores parameter values in an `AspectParameterInfo[]` for `AspectContext.Parameters`. `ref`/`out`/`in` cannot survive that capture.

```csharp
public class Svc
{
    [Log]
    public void Pull(out int x) { x = 1; }   // ❌ AOP0003
}
```

### `AOP0003B` — Aspect on method returning by ref (Error)

```csharp
private int _x;

[Log]
public ref int GetRef() => ref _x;   // ❌ AOP0003B
```

### `AOP0004` — `[AspectHandler]` type does not implement a handler interface (Error)

Reported on the `[AspectHandler(typeof(...))]` declaration itself.

```csharp
[AspectHandler(typeof(NotAHandler))]   // ❌ AOP0004
public sealed class BrokenAspectAttribute : AspectAttribute { }

public class NotAHandler { }   // doesn't implement any I*Handler
```

### `AOP0005` — Custom `AspectAttribute` missing `[AspectHandler]` (Error)

Without an `[AspectHandler]`, the generator has nothing to wire up.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class OrphanedAspectAttribute : AspectAttribute { }   // ❌ AOP0005
```

### `AOP0006` — Aspect on operator or conversion (Error)

The generator only intercepts ordinary instance methods.

```csharp
public class Box
{
    [Log]
    public static Box operator +(Box a, Box b) => new();   // ❌ AOP0006
}
```

## Tier 2 — Attribute Arguments

Reported on the attribute application itself, so the squiggle lands precisely on the wrong value.

### `[Cache]`

| ID | Severity | Trigger | Code fix |
|---|---|---|---|
| `AOP0010` | Warning | `[Cache]` on `void` / non-generic `Task` — silently suppresses subsequent calls (including side effects in the body) after the first | Remove `[Cache]` |

```csharp
[Cache]                              // ❌ AOP0010
public void DoWork() { }

[Cache]                              // ❌ AOP0010
public Task DoAsync() => Task.CompletedTask;

[Cache]                              // ✅ ok — Task<int> has a value
public Task<int> GetAsync() => Task.FromResult(1);
```

### `[Retry]`

| ID | Severity | Trigger | Code fix |
|---|---|---|---|
| `AOP0011` | Error | `MaxAttempts <= 0` — would never even execute | Set `MaxAttempts = 3` |
| `AOP0012` | Error | `DelayMs < 0` | Set `DelayMs = 0` |
| `AOP0013` | Warning | `BackoffMultiplier < 1.0` — shrinks delay between retries | Set `BackoffMultiplier = 1.0` |

```csharp
[Retry(MaxAttempts = 0)]             // ❌ AOP0011
public int A() => 1;

[Retry(DelayMs = -100)]              // ❌ AOP0012
public int B() => 1;

[Retry(BackoffMultiplier = 0.5)]     // ⚠ AOP0013 — each retry waits less than the last
public int C() => 1;
```

### `[Timeout]`

| ID | Severity | Trigger | Code fix |
|---|---|---|---|
| `AOP0014` | Error | `TimeoutMs <= 0` | Set `TimeoutMs = 30000` |
| `AOP0015` | Warning | Method has no `CancellationToken` parameter — `[Timeout]` aborts to the caller (TimeoutException) but the body keeps running in background until it finishes naturally (resource leak) | Add `CancellationToken cancellationToken = default` parameter |

```csharp
[Timeout(TimeoutMs = 0)]             // ❌ AOP0014
public Task<int> A(CancellationToken ct) => Task.FromResult(1);

[Timeout(TimeoutMs = 5000)]          // ⚠ AOP0015 — token is created but never awaited
public Task<int> B() => Task.FromResult(1);
```

### `[Validate]`

| ID | Severity | Trigger | Code fix |
|---|---|---|---|
| `AOP0016` | Warning | Method has no parameters | Remove `[Validate]` |
| `AOP0017` | Info | None of the parameters or their reachable property graph carry `DataAnnotations` | (none — diagnostic only) |

```csharp
[Validate]                           // ❌ AOP0016 — nothing to validate
public int Get() => 1;

[Validate]                           // ℹ AOP0017 — no [Required]/[Range]/...
public int Sum(int a, int b) => a + b;

[Validate]                           // ✅ ok — Order has [Range] on a property
public void Place(Order order) { }

public class Order
{
    [Range(1, 100)]
    public int Quantity { get; set; }
}
```

## Tier 3 — Call Sites

Diagnoses places where the call *would* go through the interceptor at first glance, but doesn't.

### `AOP0020` — Method group → delegate (Warning)

Converting an aspect-decorated method to `Func<>`/`Action<>`/event handler captures the original method directly. Calls through the delegate skip the interceptor entirely.

```csharp
public class Svc
{
    [Log]
    public int GetOrder(int id) => id;
}

public class Caller
{
    public Func<int, int> MakeFunc(Svc s) => s.GetOrder;   // ⚠ AOP0020
    //                                       ^^^^^^^^^^
    //   The Func captures the unwrapped method.
    //   Calling it later won't trigger [Log].
}
```

### `AOP0021` — `base.Method()` to an aspect-decorated virtual method causes infinite recursion (Error)

This was originally documented as "bypasses the aspect on the override". A behavioral
test proved the OPPOSITE: the call IS intercepted, and because the interceptor body
invokes the target via `@this.Method(...)` (virtual dispatch) it lands back in the
override, which calls `base.Method()` again — guaranteed `StackOverflowException` at
runtime. The diagnostic was bumped to **Error** and the message rewritten.

```csharp
public class Base
{
    [Log]
    public virtual int GetOrder(int id) => id;
}

public class Derived : Base
{
    public override int GetOrder(int id) => base.GetOrder(id);   // ❌ AOP0021 — guaranteed SOE
}
```

Either remove the override, remove the aspect, or reshape the call to avoid `base.`.

## Tier 4 — Convention Enforcement

Declarative architecture rules. You annotate a base class or interface with
`[RequireAspect(typeof(X))]`; the analyzer warns on every concrete derivative that doesn't
also carry `[X]`. Same idea as Metalama's architecture validation — but as one focused
attribute, no fabrics, no compile-time API.

### `AOP1001` — Type / method missing aspect required by base or interface (Warning)

The `[RequireAspect]` attribute is **placement-based** — where you put it determines
what must satisfy the rule:

#### On a class or interface — every concrete derivative needs the aspect

```csharp
[RequireAspect(typeof(LogAttribute), Reason = "All Topics must be audited")]
public abstract class Topic { }

public class OrderPlaced : Topic { }
//           ^^^^^^^^^^^^
//   ⚠ AOP1001: 'OrderPlaced' derives from 'Topic' which requires [Log].
//             Reason: All Topics must be audited.

[Log]
public class PaymentMade : Topic { }   // ✅ ok
```

Same for interfaces:

```csharp
[RequireAspect(typeof(TraceAttribute), Reason = "All command handlers must be traceable")]
public interface ICommandHandler { }

public class CreateOrderHandler : ICommandHandler { }
//           ^^^^^^^^^^^^^^^^^^
//   ⚠ AOP1001: requires [Trace]
```

#### On a method — every override / interface implementation needs the aspect

```csharp
public interface ICommandHandler
{
    [RequireAspect(typeof(TraceAttribute))]
    Task HandleAsync(object cmd);
}

public class CreateOrderHandler : ICommandHandler
{
    public Task HandleAsync(object cmd) => ...;
    //          ^^^^^^^^^^^
    //   ⚠ AOP1001: 'HandleAsync' implements 'ICommandHandler.HandleAsync' which requires [Trace]
}

public class CancelOrderHandler : ICommandHandler
{
    [Trace]
    public Task HandleAsync(object cmd) => ...;   // ✅
}
```

#### Class-level aspect satisfies a method-level requirement

This avoids false positives when the developer uses the class-level shortcut: putting
`[Trace]` on the impl class propagates to every public/internal method, so a
method-level `[RequireAspect(typeof(TraceAttribute))]` is satisfied automatically.

```csharp
[Trace]                                            // class-level → propagates to HandleAsync
public class RefundOrderHandler : ICommandHandler
{
    public Task HandleAsync(object cmd) => ...;    // ✅ satisfied via class-level [Trace]
}
```

**Code fix:** "Add [Aspect]" — inserts the attribute on its own line above the
declaration with matching indentation.

**Notes:**
- Abstract intermediate classes / abstract methods are exempt; only concrete usage sites
  are flagged.
- Multiple `[RequireAspect]` attributes on a single base produce one diagnostic per
  missing aspect — fix them one at a time with the light-bulb.
- The same requirement reachable via two paths (e.g. base class **and** interface) is
  collapsed to one diagnostic.
- Subclasses of the required aspect are accepted (e.g. `[VerboseLog : LogAttribute]`
  satisfies a `[RequireAspect(typeof(LogAttribute))]`).
- Suppress per-type with `#pragma warning disable AOP1001` if a particular derivative is
  legitimately exempt.

### `AOP1002` — Type missing interface required by base/interface (Warning)

```csharp
[RequireImplementation(typeof(IDisposable), Reason = "Connections must clean up sockets")]
public abstract class DatabaseConnection { }

public class SqlConnection : DatabaseConnection { }
//           ^^^^^^^^^^^^^
//   ⚠ AOP1002: 'SqlConnection' derives from 'DatabaseConnection' which requires
//             implementing 'IDisposable'. Reason: Connections must clean up sockets.

public class PgConnection : DatabaseConnection, IDisposable
{
    public void Dispose() { }
}                                                  // ✅ ok
```

Use this for cross-cutting capabilities (`IDisposable`, `IAsyncDisposable`, custom marker
interfaces) that the base type cannot inherit directly without forcing every member onto
the contract.

**Code fix:** "Implement {Interface}" — appends the interface to the base list. The
compiler's own CS0535 light-bulb ("Implement interface") then takes over to stub the
required members.

### `AOP1003` — Type missing method required by base/interface (Warning)

For plug-in / module conventions where the framework calls a method by name (often via
reflection) and the signature varies per host, so it can't be encoded in an abstract
member.

```csharp
[RequireMethod("Configure",
    ReturnType = typeof(void),
    Parameters = new[] { typeof(IServiceCollection) },
    Reason = "Modules must register their services")]
public abstract class Module { }

public class AuthModule : Module { }
//           ^^^^^^^^^^
//   ⚠ AOP1003: requires 'void Configure(IServiceCollection)'. Reason: Modules must register their services.

public class OrderModule : Module
{
    public void Configure(IServiceCollection services) { }   // ✅
}
```

`ReturnType` and `Parameters` are optional — when omitted, only the method name is
checked. Methods inherited from intermediate base classes satisfy the rule, so a base
class can ship a virtual default implementation without tripping the analyzer.

No code fix — generating method stubs is too opinionated about body shape; the analyzer
just points you at the missing method.

## Code Fix Summary

Eleven of the diagnostics ship a Roslyn code fix you can apply with Alt+Enter / Cmd+. :

| Diagnostic | Code fix |
|---|---|
| `AOP0001` | Remove aspect attribute from static method |
| `AOP0002` | Change accessibility to `internal` |
| `AOP0010` | Remove `[Cache]` from non-returning method |
| `AOP0011` | Set `MaxAttempts = 3` |
| `AOP0012` | Set `DelayMs = 0` |
| `AOP0013` | Set `BackoffMultiplier = 1.0` |
| `AOP0014` | Set `TimeoutMs = 30000` |
| `AOP0015` | Add `CancellationToken cancellationToken = default` parameter |
| `AOP0016` | Remove `[Validate]` from parameterless method |
| `AOP1001` | Add `[Aspect]` attribute |
| `AOP1002` | Implement {Interface} (append to base list) |

The remaining diagnostics are intentionally fix-less — repairing them either requires an API redesign (`AOP0003`/`AOP0006`), depends on user intent (`AOP0017`), or describes legitimate code that should just be reviewed (`AOP0020`/`AOP0021`).

## Suppression

Standard Roslyn suppression works:

```csharp
#pragma warning disable AOP0015
[Timeout(TimeoutMs = 5000)]
public async Task<int> WorkAsync() { ... }   // can't add CT to interface contract
#pragma warning restore AOP0015
```

Or in `.editorconfig`:

```ini
# Globally downgrade AOP0017 (Validate without DataAnnotations) to silent.
dotnet_diagnostic.AOP0017.severity = none
```

## Why analyzers, not just runtime checks?

Three reasons:

1. **Speed of feedback.** The runtime would discover `[Cache]` on a `void` method only when that method was first called — and even then, the result is just "cache silently doesn't help". Analyzers fail in the editor, in seconds.
2. **Locatable errors.** A runtime exception from inside a generated interceptor points at generated code; an analyzer diagnostic points at the exact attribute or call site.
3. **No false friends.** Many of these mistakes (negative `DelayMs`, `MaxAttempts = 0`, method without `CancellationToken`) don't *crash* — they just don't do what you wanted. Analyzers refuse the bug instead of letting it run.
