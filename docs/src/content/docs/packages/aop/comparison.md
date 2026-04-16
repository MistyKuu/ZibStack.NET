---
title: ZibStack.NET.Aop — Alternatives
description: "How ZibStack.NET.Aop compares to PostSharp, Metalama, Castle.DynamicProxy, and standalone Polly/HybridCache — dispatch mechanism, licensing, built-in aspects."
---

.NET AOP has three historical waves:

1. **MSIL rewriters** — PostSharp (2008, commercial)
2. **Runtime proxies** — Castle.DynamicProxy (free, but requires `virtual`/interface)
3. **Roslyn-native source gen** — Metalama (2023, commercial) and now this package

Resilience patterns (retry / timeout / circuit-breaker / cache) have their own fluent libraries — Polly and Microsoft.Extensions.Caching.Hybrid — that ZibStack.NET.Aop wraps with a declarative attribute instead of fluent plumbing at every call site.

| Feature | **ZibStack.NET.Aop** | PostSharp | Metalama | Castle.DynamicProxy | Polly / HybridCache |
|---|---|---|---|---|---|
| Dispatch | Roslyn source gen + C# 12 interceptors | MSIL post-processing | Roslyn + T# template | Runtime proxy gen | Direct API calls |
| Price | ✅ MIT free | Commercial (paid) | Commercial (free tier) | MIT free | MIT free |
| Needs `virtual`/interface? | ❌ works on any method | ❌ | ❌ | ✅ yes — can't wrap sealed/non-virtual | n/a (wrapping) |
| Runtime reflection | ❌ | ❌ | ❌ | ✅ proxy generation | ❌ |
| Overhead per call | **zero dispatch** (direct inline via interceptor) | near zero | near zero | proxy + dispatch | single delegate |
| Declarative attributes | ✅ `[Retry]`, `[Timeout]`, `[Trace]`, `[Cache]`, … | ✅ | ✅ | ❌ (wire via DI container) | ❌ (fluent API) |
| Retry / timeout / circuit-breaker | ✅ built-in + Polly bridge (`[PollyRetry]`, `[PollyCircuitBreaker]`, `[PollyRateLimiter]`, `[HttpRetry]`) | custom aspect | custom aspect | custom interceptor | ✅ built-in |
| HybridCache (.NET 9+) | ✅ `[HybridCache]` attribute | n/a | n/a | n/a | ✅ library API |
| Entry/exit/exception logging | ✅ `[Log]` (via ZibStack.NET.Log inline emitter — zero alloc) | custom aspect | custom aspect | n/a | n/a |
| OpenTelemetry Activities | ✅ `[Trace]` | custom aspect | custom aspect | n/a | n/a |
| `System.Diagnostics.Metrics` | ✅ `[Metrics]` | custom aspect | custom aspect | n/a | n/a |
| Project-wide defaults | ✅ fluent `IAopConfigurator` | via convention | via fabric class | n/a | fluent per-client |
| Source-visible generated code | ✅ `EmitCompilerGeneratedFiles=true` writes `.g.cs` | ❌ (IL only) | ✅ T# previews | n/a | n/a |
| Analyzer feedback at write time | ✅ AOP0xxx diagnostics | via build step | ✅ real-time | n/a | n/a |

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
