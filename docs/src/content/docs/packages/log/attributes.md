---
title: Attribute reference
description: "Attribute and configuration reference for ZibStack.NET.Log — interpolation settings and ILogConfigurator."
---

## Attribute Reference

### `[Log]` attribute (AOP package)

The `[Log]` attribute and its properties (`[Sensitive]`, `[NoLog]`, `EntryExitLevel`, `ObjectLogMode`, etc.) are now documented in the AOP package:

**See: [AOP — Log Attribute](/ZibStack.NET/packages/aop/log-attribute/)** for the full `[Log]` reference.

### Interpolation configuration (`ILogConfigurator`)

Settings for `logger.LogXxx($"...")` interpolated-string logging go through `ILogConfigurator`. This interface has moved to the `ZibStack.NET.Aop` namespace:

```csharp
using ZibStack.NET.Aop;  // ILogConfigurator is in the Aop namespace

public sealed class LogConfig : ILogConfigurator
{
    public void Configure(ILogBuilder b)
    {
        b.Interpolation(i => i.PropertyNameCasing = ZibLogPropertyCasing.CamelCase);
    }
}
```

| Property | Default | Description |
|---|---|---|
| `PropertyNameCasing` | `PascalCase` | Casing of structured property names in log templates (`PascalCase` or `CamelCase`) |

### Interpolated string logging

With `using ZibStack.NET.Log;`, standard `LogXxx` methods accept `$"..."` with structured logging:

| Method | Description |
|---|---|
| `_logger.LogTrace($"...")` | Trace level with structured properties |
| `_logger.LogDebug($"...")` | Debug level |
| `_logger.LogInformation($"...")` | Information level |
| `_logger.LogWarning($"...")` | Warning level |
| `_logger.LogError($"...")` | Error level |
| `_logger.LogError(ex, $"...")` | Error level with exception |
| `_logger.LogCritical($"...")` | Critical level |
| `_logger.LogCritical(ex, $"...")` | Critical level with exception |


## Requirements

- **.NET 8.0** or later (uses C# interceptors)

That's it. The package's `build/.props` enables `InterceptorsNamespaces` automatically — you don't need to touch your `.csproj`.

## Roadmap

No open items. Have a feature request? [Open an issue](https://github.com/MistyKuu/ZibStack.NET.Log/issues).

## License

MIT
