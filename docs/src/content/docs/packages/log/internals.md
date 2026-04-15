---
title: How interpolated logs are rewritten
description: Internals — the interpolated string handler shadowing trick + the source-generated interceptor that caches the delegate. Why LogInformation($"...") is finally cheap.
---

## In-depth: how `LogInformation($"...")` actually works

There are **two independent questions** about interpolated-string structured logging with ZibStack.NET.Log, and the answer to each one is different:

1. **Why does `logger.LogInformation($"Hey {user}")` preserve structured properties instead of flattening to a string?** → Handled by a C# 11 interpolated-string handler + extension-method shadowing. This is the primary mechanism.
2. **Why is it zero-allocation and ~5× faster than `LogInformation("Hey {user}", user)`?** → Handled by a source-generated interceptor that emits a cached `LoggerMessage.Define<T>` delegate per call site.

You can have the first without the second. You can't have the second without the first. Let's walk through both.

### Layer 1 — extension method shadowing (the primary mechanism)

The package ships a set of extension methods on `ILogger` in `ZibStack.NET.Log`:

```csharp
// ZibStack.NET.Log.Abstractions / LoggerStructuredExtensions.cs
public static void LogInformation(
    this ILogger logger,
    [InterpolatedStringHandlerArgument("logger")]
    ref ZibLogInformationHandler handler)
{ /* body — see Layer 2 */ }
```

When the compiler sees `logger.LogInformation($"Hey {user}")`, overload resolution picks **this** overload over Microsoft's `LoggerExtensions.LogInformation(this ILogger, string, params object[])` — **only because the argument is an interpolated string**. C# 11 preferentially routes interpolated strings to overloads that accept an `[InterpolatedStringHandler]` type when one is in scope. If you passed a plain string (`logger.LogInformation("hello")`), the Microsoft overload still wins.

This is called **extension method shadowing**, and it requires exactly one thing at the call site: **`ZibStack.NET.Log` must be in scope**. Either:

- `using ZibStack.NET.Log;` at the top of the file, or
- `<ZibLogEmitGlobalUsing>true</ZibLogEmitGlobalUsing>` in your `.csproj` (or the `ZibLogStrict` umbrella switch — see [Configuration](#configuration)).

Without the `using`, Microsoft's overload wins and you get the standard flat-string behavior. With the `using`, our overload wins and Layer 2 kicks in.

### What the handler does before anything else runs

`ZibLogInformationHandler` is a `[InterpolatedStringHandler]` `ref struct`. Its constructor takes the logger and writes an `out bool shouldAppend`:

```csharp
public ZibLogInformationHandler(
    int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
{
    IsEnabled = logger.IsEnabled(LogLevel.Information);
    shouldAppend = IsEnabled;   // ← tells the compiler whether to run AppendFormatted calls
    if (IsEnabled && formattedCount > 6)
        FallbackArgs = new object?[formattedCount];
}
```

If `shouldAppend` is `false`, **the compiler skips every `AppendFormatted` call**. That's a built-in feature of C# 11 interpolated string handlers, not something we wrote. So for a disabled-level call like `logger.LogDebug($"slow string {ExpensiveToString()}")`, `ExpensiveToString()` **is never evaluated** — the compiler emits an `if (shouldAppend) { … }` guard around the whole append block. This is where the ~3.2 ns "disabled path" comes from.

If `shouldAppend` is `true`, the compiler emits one `AppendFormatted` call per interpolation hole. Our handler stores each argument into a **typed slot** matched to its static type:

```csharp
public long L0, L1, L2, L3, L4, L5;        // int / long / bool / byte / short / char / uint
public double D0, D1, D2, D3;               // double / float
public decimal M0, M1;                       // decimal
public string? S0, S1, S2, S3, S4, S5;       // string
public object? O0, O1;                       // custom types (last resort, boxes)

public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "")
    => StoreLong(v);
// … 15+ more overloads per numeric / string / object type
```

Three things to notice:

1. **Zero boxing for primitives.** `int` is stored as `long` in `L0..L5`, not as `object` in an array. Compare with Microsoft's `LogInformation("Hey {user}", int)` which wraps `int` in `object[]` → one boxing per argument.
2. **`CallerArgumentExpression` captures the variable name.** `$"Hey {UserId}"` → `AppendFormatted` receives `name = "userId"` for free. That's how the handler knows the structured property name without any runtime reflection — the C# compiler bakes the expression text into the call site.
3. **Format specifiers are preserved.** Each `AppendFormatted` has a `string? format` overload that receives `"C"` for `$"{total:C}"`, `"P2"` for `$"{ratio:P2}"`, etc. — keeping the full template round-trippable.

`AppendLiteral(string s)` is a no-op: we don't need to accumulate the literal text because we're never building a flat string. The literals are re-assembled later from a compile-time template, not from handler state.

**At this point**, whether or not a source generator runs, the handler has everything it needs for structured logging: typed values, structured names, and the format template (conceptually — the template itself is only materialized in Layer 2).

### Layer 2 — source-generated interceptor (the cache optimization)

The handler's extension method has a **no-op body** in the shipped assembly:

```csharp
public static void LogInformation(
    this ILogger logger,
    [InterpolatedStringHandlerArgument("logger")]
    ref ZibLogInformationHandler handler)
{
    // Replaced by generator-emitted [InterceptsLocation] interceptor.
}
```

> **Safety net.** If someone references only `ZibStack.NET.Log.Abstractions` without the generator, the extension method body throws `InvalidOperationException("Install the full ZibStack.NET.Log package")` at the first enabled log call — no silent data loss. In practice the Abstractions package is only referenced transitively by other ZibStack generators; end users always install `ZibStack.NET.Log` which ships both halves.

At compile time, the source generator in `ZibStack.NET.Log` scans every `logger.LogXxx($"...")` call site in your project and emits one interceptor per call site:

```csharp
// Generated file (simplified):
file static class __Interceptors_MyFile
{
    // Cached once at static init — ONE allocation per call site for the whole process
    private static readonly Action<ILogger, int, Exception?> __logger_0 =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, "Hey"),
            "Hey {UserId}");   // ← literal template, parsed once by LoggerMessage.Define

    [InterceptsLocation(version: 1, "…base64 hash of file+line+column…")]
    public static void __LogInformation_0(
        this ILogger logger,
        ref ZibLogInformationHandler handler)
    {
        if (!handler.IsEnabled) return;
        __logger_0(logger, (int)handler.L0, null);
    }
}
```

`[InterceptsLocation]` tells the compiler: "every time you see a call at the original source location, rewrite it to call this method instead". Your original `logger.LogInformation($"Hey {UserId}")` compiles **as if you wrote** `__Interceptors_MyFile.__LogInformation_0(logger, ref handler)`.

What the interceptor adds beyond what the handler already had:

- **Cached `LoggerMessage.Define<T>` delegate** — the template `"Hey {UserId}"` is parsed exactly once (at static init) instead of on every call. Microsoft's internal `FormattedLogValues` parses the template on every call into a fresh `LogValuesFormatter` — that's where most of the legacy overhead lives.
- **Typed dispatch** — `__logger_0(logger, (int)handler.L0, null)` is a direct strongly-typed call. No `object[]`, no boxing, no allocation.
- **One delegate per call site**, not per call — the cost is paid once at static init, amortized across every invocation for the process lifetime.

### What you get at each layer

| Property | Microsoft `LogInformation("Hey {x}", x)` | ZibStack handler alone (no interceptor) | ZibStack handler + interceptor |
|---|---|---|---|
| Structured properties preserved | ✓ | ✓ (from handler + `CallerArgumentExpression`) | ✓ |
| Lazy evaluation when disabled | ✓ via `IsEnabled` | ✓ via `shouldAppend` (skips `AppendFormatted`) | ✓ |
| Zero boxing for primitives | ✗ (`object[]`) | ✓ (typed slots) | ✓ |
| Template parsed once, then cached | ✗ (runtime per call) | ✗ | ✓ (`LoggerMessage.Define` at static init) |
| Dispatch allocation per call | `FormattedLogValues` + `object[]` | throws (generator required) | zero |
| Disabled-level cost | ~16 ns | ~3.2 ns (`shouldAppend` check only) | ~3.2 ns |
| Enabled-level cost (NullLogger) | ~19 ns | throws | ~3.8 ns |

Combined result: **~5× faster than Microsoft in both enabled and disabled paths, with zero allocation.** The handler's `shouldAppend` gives us the lazy-eval win; the interceptor's cached delegate gives us the zero-alloc-dispatch win. Microsoft's `LogInformation("template", args)` allocates 104 B (the `params object[]`) even when the level is disabled.

### Why not just use `IFormattedMessage` / handler alone?

The natural instinct is "the handler already has the typed values — why do we need a generator at all? Can't the extension-method body just call `logger.Log(...)` directly?"

It can, but each of the three natural shapes sacrifices something:

1. **Build a flat string and call `logger.Log(level, eventId, formatted, null, (s, _) => s)`** — structured properties are lost. The logger sees `"Hey 42"`, not `{ Template: "Hey {UserId}", userId: 42 }`. This is the failure mode the whole library exists to avoid.
2. **Build an `object?[]` from the slots and call `logger.Log(level, eventId, new FormattedLogValues(template, args), …)`** — boxing comes back. The typed-slot win vanishes. Also: `FormattedLogValues` is internal to Microsoft.Extensions.Logging; you can't construct it directly, so you'd call `logger.LogInformation(template, args)` which goes back through the same parse-every-time code path.
3. **Call `LoggerMessage.Define<T>(level, eventId, template)(logger, value, null)` in the extension method body** — this works, but `LoggerMessage.Define` is expensive (it parses the template and builds a `LogValuesFormatter` internally). Doing it on every call is worse than the Microsoft flat path.

The only way to get **all three** of structured + typed + cached is to cache `LoggerMessage.Define` **per call site**, which means you need a separate field per call site, which means you need code generation. That's exactly what the interceptor does — it promotes a single shared extension method into one dedicated method per call site, each with its own cached delegate field.

TL;DR — the handler does 80% of the work. The interceptor is the "pay-once, reuse-forever" cache that gets you the last 20%, and it's particularly visible on the disabled-level path where every nanosecond compounds across hot loops.

