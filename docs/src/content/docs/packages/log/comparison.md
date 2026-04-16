---
title: ZibStack.NET.Log — Alternatives
description: "How ZibStack.NET.Log compares to plain Microsoft.Extensions.Logging with $\"…\" and the [LoggerMessage] source generator — the only tools that actually address the same surface (compile-time rewrite of interpolated log calls into structured templates)."
---

ZibStack.NET.Log is a **compile-time rewrite layer** for the call-site pattern `log.LogInformation($"…")`. That's a narrow surface — only two tools in the ecosystem address it today:

1. **Plain `Microsoft.Extensions.Logging` with `$"…"`** — works, but CA2254 warns, and the template gets flattened to a single string so structured logging loses property names and every call allocates ~104 B.
2. **`[LoggerMessage]` source generator (.NET 6+)** — zero-alloc, structured, but every log message needs its own declared partial method with named parameters.

**Serilog / NLog / Seq / Application Insights are not alternatives** — they are **log sinks / backends**. ZibStack.NET.Log plugs into `ILogger`, so whichever provider you've wired up (Serilog / NLog / file / console / Seq / Elastic / Loki) keeps receiving the logs, just now with properly preserved structured templates and no hot-path allocation.

| Feature | **ZibStack.NET.Log** | Microsoft.Extensions.Logging + `$"…"` | `[LoggerMessage]` (.NET 6+) |
|---|---|---|---|
| Call-site style | `log.LogInformation($"user {id}")` | `log.LogInformation($"user {id}")` | `[LoggerMessage] partial void LogUser(int id);` then `LogUser(id);` |
| Preserves template (structured) | ✅ rewritten to `"user {Id}", id` at compile time | ❌ flattens to one string | ✅ compile-time |
| CA2254 warning on `$"..."` | ✅ silenced — structured preserved | ⚠️ yes | n/a (different syntax) |
| Alloc per call (level off) | **0 B** | 104 B | 0 B |
| Per-call latency (level off) | **~3 ns** | ~15 ns | ~3 ns |
| Zero boilerplate new call sites | ✅ (just write `$"…"`) | ✅ | ❌ declare each `[LoggerMessage]` partial method |
| Automatic entry/exit/exception | ✅ `[Log]` attribute | ❌ | ❌ |
| Parameter masking / exclusion | ✅ `[Sensitive]` / `[NoLog]` | ❌ | ❌ |
| Project-wide defaults | ✅ fluent `ILogConfigurator` | `appsettings.json` log levels only | n/a |

## What you give up

Nothing over raw `ILogger` — ZibStack.NET.Log **is** `ILogger`. Your existing Serilog / NLog / Seq / Application Insights configuration stays unchanged; logs flow through whatever provider is registered in DI. This is additive, not a replacement.

## When to pick ZibStack.NET.Log

You like `$"…"` ergonomics (clean, refactor-safe, IDE-friendly) but you need **zero-alloc + structured output** + the CA2254 warning gone, without rewriting every call site to `"template", args`. The `[Log]` attribute then saves the per-method boilerplate of entry/exit/exception logging — one line on a method, full instrumentation.

## When to stay on the two real alternatives

- **`[LoggerMessage]` directly** — you prefer the explicit declarative pattern with hand-named partial methods and don't mind the per-signature boilerplate. Fine choice; same zero-alloc profile.
- **Microsoft.Extensions.Logging vanilla with `"template", args`** — tiny CLI tools with a handful of log statements where 104 B per call doesn't matter and structured logging isn't a priority.

## Sources

- [CA2254: Template should be a static expression (Microsoft docs)](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2254)
- [High-performance logging (Microsoft docs)](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/high-performance-logging)
- [LoggerMessage attribute (Microsoft docs)](https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator)
