---
title: "[Log] attribute"
description: "The [Log] attribute is now part of ZibStack.NET.Aop. This page redirects to the AOP documentation."
---

## `[Log]` attribute has moved

The `[Log]` attribute for method-level entry/exit/error logging is now handled by the **ZibStack.NET.Aop** package.

See the full documentation at: **[AOP — Log Attribute](/ZibStack.NET/packages/aop/log-attribute/)**

### Quick summary

- Install `ZibStack.NET.Aop` (not `ZibStack.NET.Log`) to use `[Log]`
- Call `app.Services.UseAop()` at startup
- The `ZibStack.NET.Log` package provides only interpolated-string logging (`_logger.LogInformation($"...")`)

### Migration

If you previously installed `ZibStack.NET.Log` for the `[Log]` attribute, add `ZibStack.NET.Aop`:

```bash
dotnet add package ZibStack.NET.Aop
```

No code changes needed — the attribute API is the same. The AOP package handles code generation for `[Log]`.
