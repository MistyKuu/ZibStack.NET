---
title: "[Log] attribute & diagnostics"
description: How the method-level [Log] attribute generates entry/exit logs, plus diagnostic IDs the analyzer surfaces.
---

## `[Log]` attribute — how that works (different mechanism)

The `[Log]` attribute uses a **completely different** code generation path from the interpolated-string handler — don't mix them up.

ZibStack.NET.Log's `[Log]` generator runs at compile time:

1. Finds classes marked `[Log]` and methods marked `[Log]`
2. For each decorated method, emits an interceptor that wraps the original call with entry/exit/exception logging
3. The wrapper uses `LoggerMessage.Define` (zero-allocation) and resolves the `ILogger<T>` from DI (cached after first resolve)

```
Your code                          Generated interceptor
──────────                         ─────────────────────
service.PlaceOrder(42, "Widget")   →   PlaceOrder_ZibStack.Log(@this, 42, "Widget")
                                       {
                                           log entry (LoggerMessage.Define)
                                           stopwatch.Start()
                                           try {
                                               result = @this.PlaceOrder(42, "Widget")
                                               log exit + elapsed
                                           } catch {
                                               log error + elapsed
                                               throw
                                           }
                                       }
```

**No reflection. No IL weaving. No runtime proxy. No virtual methods. No partial classes.** The generated code is identical to what you'd write by hand with `LoggerMessage.Define`.

The `[Log]` attribute and interpolated-string logging are orthogonal — you can use either, both, or neither in the same project. They share only the `LoggerMessage.Define` machinery they both emit.

## Diagnostics

ZibStack.NET.Log reports clear compiler errors when something is misconfigured:

| Code | Description |
|---|---|
| SL0005 | `[Log]` on static method (not supported) |

