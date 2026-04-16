---
title: ZibStack.NET.Log — Alternatives
description: "How ZibStack.NET.Log compares to Microsoft.Extensions.Logging + $\"...\", the [LoggerMessage] source generator, Serilog, and NLog — allocation, structured-logging fidelity, and the CA2254 story."
---

Structured logging in .NET has several mature options. ZibStack.NET.Log **isn't a replacement for them** — it plugs into `ILogger`, so any of these sinks/backends still work. It's a **compile-time rewrite layer** that changes *how the call sites look* (`$"..."` ergonomics) without changing where the logs end up.

| Feature | M.E.Logging + `$"..."` | `[LoggerMessage]` (.NET 6+) | Serilog | NLog | **ZibStack.NET.Log** |
|---|---|---|---|---|---|
| Call-site style | `log.LogInformation($"user {id}")` | `[LoggerMessage] partial void LogUser(int id);` | `log.Information("user {Id}", id)` | `log.Info("user {0}", id)` | `log.LogInformation($"user {id}")` |
| Preserves template (structured) | ❌ flattens to one string | ✅ compile-time | ✅ | ✅ | ✅ |
| CA2254 warning on `$"..."` | ⚠️ yes | n/a | n/a | n/a | ✅ silenced — structured preserved |
| Alloc per call (level off) | 104 B | 0 B | ~80–160 B | ~80 B | **0 B** |
| Per-call latency (level off) | ~15 ns | ~3 ns | ~25 ns | ~30 ns | **~3 ns** |
| Zero boilerplate new call sites | ✅ | ❌ declare each `[LoggerMessage]` | ✅ | ✅ | ✅ |
| Automatic entry/exit/exception | ❌ | ❌ | ❌ | ❌ | ✅ `[Log]` attribute |
| Parameter masking / exclusion | ❌ | ❌ | custom enricher | ❌ | ✅ `[Sensitive]` / `[NoLog]` |
| Sink ecosystem (Seq/Elastic/Loki) | via adapters | via adapters | ✅ huge native | ✅ huge native | ✅ any `ILogger` sink (Serilog/NLog/Seq/…) |
| Project-wide defaults | appsettings log levels only | n/a | `LoggerConfiguration` | XML config | ✅ fluent `ILogConfigurator` |

## What you give up

Nothing over raw `ILogger` — ZibStack.NET.Log **is** `ILogger`. Your existing Serilog/Seq/Application Insights/etc. configuration stays unchanged; logs go through whatever provider is registered in DI.

## When to pick ZibStack.NET.Log

You like `$"..."` ergonomics (clean, refactor-safe, IDE-friendly) but you need **zero-alloc + structured output** + the CA2254 warning gone, without rewriting every call site to `"template", args`. The `[Log]` attribute then saves the tens-of-methods boilerplate of entry/exit/exception logging — one line on a method, full instrumentation.

## When to stay on alternatives

- **`[LoggerMessage]` directly** — you prefer the explicit declarative pattern with hand-named partial methods and don't mind the per-signature boilerplate.
- **Serilog/NLog** — you already have a working template-style codebase (`"user {Id}", id`) with no hot-path allocation concerns, and no desire to let newcomers use `$"..."` safely.
- **Microsoft.Extensions.Logging vanilla** — tiny CLI tools with a handful of log statements where 104 B × few calls doesn't matter.

## Sources

- [CA2254: Template should be a static expression (Microsoft docs)](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2254)
- [High-performance logging (Microsoft docs)](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/high-performance-logging)
- [Serilog](https://serilog.net/)
- [NLog](https://nlog-project.org/)
