---
title: ZibStack.NET.Aop — Alternatives
description: "How ZibStack.NET.Aop compares to PostSharp, Metalama, Castle.DynamicProxy, and standalone Polly/HybridCache — dispatch mechanism, licensing, built-in aspects."
---

.NET AOP has three historical waves:

1. **MSIL rewriters** — PostSharp (2008, commercial)
2. **Runtime proxies** — Castle.DynamicProxy (free, but requires `virtual`/interface)
3. **Roslyn-native source gen** — Metalama (2023, commercial) and now this package

Resilience patterns (retry / timeout / circuit-breaker / cache) have their own fluent libraries — Polly and Microsoft.Extensions.Caching.Hybrid — that ZibStack.NET.Aop wraps with a declarative attribute instead of fluent plumbing at every call site.

| Feature | PostSharp | Metalama | Castle.DynamicProxy | Polly / HybridCache (standalone) | **ZibStack.NET.Aop** |
|---|---|---|---|---|---|
| Dispatch | MSIL post-processing | Roslyn + T# template | Runtime proxy gen | Direct API calls | Roslyn source gen + C# 12 interceptors |
| Price | Commercial (paid) | Commercial (free tier) | MIT free | MIT free | ✅ MIT free |
| Needs `virtual`/interface? | ❌ | ❌ | ✅ yes — can't wrap sealed/non-virtual | n/a (wrapping) | ❌ works on any method |
| Runtime reflection | ❌ | ❌ | ✅ proxy generation | ❌ | ❌ |
| Overhead per call | near zero | near zero | proxy + dispatch | single delegate | **zero dispatch** (direct inline via interceptor) |
| Declarative attributes | ✅ | ✅ | ❌ (wire via DI container) | ❌ (fluent API) | ✅ `[Retry]`, `[Timeout]`, `[Trace]`, `[Cache]`, … |
| Retry / timeout / circuit-breaker | custom aspect | custom aspect | custom interceptor | ✅ built-in | ✅ built-in + Polly bridge (`[PollyRetry]`, `[PollyCircuitBreaker]`, `[PollyRateLimiter]`, `[HttpRetry]`) |
| HybridCache (.NET 9+) | n/a | n/a | n/a | ✅ library API | ✅ `[HybridCache]` attribute |
| Entry/exit/exception logging | custom aspect | custom aspect | n/a | n/a | ✅ `[Log]` (via ZibStack.NET.Log inline emitter — zero alloc) |
| OpenTelemetry Activities | custom aspect | custom aspect | n/a | n/a | ✅ `[Trace]` |
| `System.Diagnostics.Metrics` | custom aspect | custom aspect | n/a | n/a | ✅ `[Metrics]` |
| Project-wide defaults | via convention | via fabric class | n/a | fluent per-client | ✅ fluent `IAopConfigurator` |
| Source-visible generated code | ❌ (IL only) | ✅ T# previews | n/a | n/a | ✅ `EmitCompilerGeneratedFiles=true` writes `.g.cs` |
| Analyzer feedback at write time | via build step | ✅ real-time | n/a | n/a | ✅ AOP0xxx diagnostics |

## What you give up

Metalama's T# is more powerful for custom aspects that rewrite method bodies in complex ways — our extension model is simpler (`IAsyncAroundAspectHandler`, `IAspectHandler`) and doesn't expose a template language. For exotic compile-time transformations (changing control flow, injecting new members, editing existing syntax trees) you'd still pick Metalama.

## When to pick ZibStack.NET.Aop

You want **declarative cross-cutting concerns** (retry, timeout, cache, trace, metrics, logging) on any method, with zero runtime cost and without paying for PostSharp/Metalama or the `virtual` + container dance of Castle.DynamicProxy. The `[PollyRetry]` / `[HybridCache]` bridges give you the best-in-class resilience/cache libs with an attribute instead of fluent plumbing at every call site.

## When to stay on alternatives

- **Polly / HybridCache directly** — you build retry/cache strategies at runtime based on non-compile-time data (dynamic configs, per-request policies), or you want one handler wrapping multiple method calls at a single DI seam.
- **Metalama** — you're writing custom aspects that heavily rewrite method bodies, want the T# template language, and commercial licensing is fine.
- **Castle.DynamicProxy** — you need runtime-decided aspects (interceptors chosen based on runtime config), have interfaces everywhere, and don't care about the proxy allocation.

## Sources

- [The State of Aspect-Oriented Programming in C# — PostSharp blog](https://blog.postsharp.net/state-of-aop)
- [Metalama documentation](https://doc.metalama.net/)
- [Castle.DynamicProxy](https://github.com/castleproject/Core)
- [Polly](https://github.com/App-vNext/Polly)
- [Microsoft.Extensions.Caching.Hybrid](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid)
