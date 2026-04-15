---
title: Benchmarks
description: Cost numbers for interpolated rewriting + masking vs hand-written ILogger calls. Why the rewriter is cheap (zero-alloc on disabled level).
---

## Benchmarks

Overhead of calling `int Add(int a, int b) => a + b;` with logging, BenchmarkDotNet on .NET 10.0:

| Method | Mean | Allocated |
|---|---:|---:|
| No logging (baseline) | 0.0 ns | 0 B |
| Manual `LoggerMessage.Define` (level OFF) | 32.3 ns | 0 B |
| Manual `LoggerMessage.Define` | 34.7 ns | 0 B |
| **ZibStack.Log `[Log]` no stopwatch** | **34.9 ns** | **64 B** |
| **ZibStack.Log `[Log]` (level OFF)** | **38.3 ns** | **64 B** |
| **ZibStack.Log `[Log]`** | **38.8 ns** | **64 B** |
| `[Log]` return object (no `[Sensitive]`) | 40.4 ns | 96 B |
| Manual `ILogger.Log()` (level OFF) | 62.9 ns | 176 B |
| Manual `ILogger.Log()` | 68.6 ns | 176 B |
| `[Log]` return object (with `[Sensitive]`) | 96.8 ns | 624 B |

- **ZibStack.Log ≈ hand-written `LoggerMessage.Define`** — same tier (~39 ns vs ~35 ns)
- **~2x faster** than `_logger.LogInformation()` (39 ns vs 69 ns)
- **2.8x less memory** than `_logger.LogInformation()` (64 B vs 176 B)
- When log level OFF: ~38 ns (just DI resolve + `IsEnabled` check)

### Property-level sanitization overhead

When return type has `[Sensitive]`/`[NoLog]` properties (Dictionary + JSON serialization):

| Method | Mean | Allocated |
|---|---:|---:|
| `[Log]` return object (no `[Sensitive]`) | 40.4 ns | 96 B |
| `[Log]` return object (with `[Sensitive]`) | 96.8 ns | 624 B |

+56 ns and +528 B per call for sanitization. Use `[Sensitive]` on properties only where needed.

### Interpolated string logging

`LogInformation($"...")` vs standard `LogInformation("template", args)` — pure overhead measurement with NullLoggerProvider (no sink, measures handler + dispatch cost only):

| Method | Mean | Allocated |
|---|---:|---:|
| **`LogInformation($"...")` (level OFF)** | **3.2 ns** | **0 B** |
| **`LogInformation($"...")` (level ON)** | **3.8 ns** | **0 B** |
| `LogInformation("template", args)` (level OFF) | 15.7 ns | 104 B |
| `LogInformation("template", args)` (level ON) | 19.1 ns | 104 B |

**Key results:**

1. **~5× faster when the level is disabled.** The interpolated-string handler's constructor checks `IsEnabled` and writes `shouldAppend = false` — the compiler skips every `AppendFormatted` call. Cost: ~3.2 ns vs ~15.7 ns for Microsoft's path (which allocates `params object[]` before the level check). **Zero allocation** in both enabled and disabled paths.
2. **~5× faster when enabled.** With a NullLoggerProvider (logging enabled but output discarded), our interceptor dispatches through a cached `LoggerMessage.Define` delegate at ~3.8 ns / 0 B vs Microsoft's ~19.1 ns / 104 B. The typed-slot handler avoids boxing entirely; Microsoft boxes every argument into `object[]`.

For zero-allocation logging in hot paths, prefer the `[Log]` attribute (~39 ns, 64 B) — it generates a cached `LoggerMessage.Define<T>` delegate per method and bypasses the interpolated string handler entirely.

**How it works:** a two-layer mechanism — see the [In-depth: how `LogInformation($"...")` actually works](#in-depth-how-loginformation-actually-works) section below for the full picture. Short version: a C# 11 interpolated-string handler (`ref struct ZibLogInformationHandler`) shadows Microsoft's `LogXxx(string, params object[])` overload via extension-method resolution and captures arguments into typed slots (zero boxing for primitives, `IsEnabled` checked lazily). The source generator then emits a per-call-site `[InterceptsLocation]` interceptor that reads those slots and dispatches through a **cached** `LoggerMessage.Define<T1, T2, T3>` delegate — one allocation at static init, zero allocations per call.

### Analyzer: detect legacy log calls

The `ZLOG002` analyzer ships inside the main `ZibStack.NET.Log` package — no extra install needed. It warns whenever your code uses the legacy `_logger.LogXxx("template {Param}", value)` form. Only `Microsoft.Extensions.Logging.ILogger` extension calls with a string literal + at least one trailing argument are flagged, so plain messages and already-interpolated calls are left alone.

```csharp
_logger.LogInformation("User {Name}", name);
//      ^^^^^^^^^^^^^^
// ⚠ ZLOG002: Replace 'LogInformation("template", args)' with 'LogInformation($"template")'
//   — the ZibStack.NET.Log generator emits a typed LoggerMessage.Define interceptor
//     (~5x faster, zero allocation vs Microsoft's 104 B)
```

