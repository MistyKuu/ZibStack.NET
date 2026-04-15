---
title: Attribute reference
description: Full parameter list for every attribute — [Log], [Sensitive], [NoLog], [ZibLogDefaults] — defaults, behavior, examples.
---

## Attribute Reference

### Automatic method/class logging (`[Log]`)

| Attribute | Target | Default | Description |
|---|---|---|---|
| `[Log]` | Method | | Adds entry/exit/exception logging |
| `[Log(EntryExitLevel = ...)]` | Method | `Information` | Log level for entry/exit (`ZibLogLevel.*`) |
| `[Log(ExceptionLevel = ...)]` | Method | `Error` | Log level for exceptions (`ZibLogLevel.*`) |
| `[Log(LogParameters = false)]` | Method | `true` | Log parameter values on entry |
| `[Log(MeasureElapsed = false)]` | Method | `true` | Measure elapsed time with Stopwatch |
| `[Log(EntryMessage = "...")]` | Method | auto | Custom entry message template |
| `[Log(ExitMessage = "...")]` | Method | auto | Custom exit message template |
| `[Log(ExceptionMessage = "...")]` | Method | auto | Custom exception message template |
| `[Log(ObjectLogging = ...)]` | Method | `Destructure` | How complex objects are logged (`ObjectLogMode.*`) |
| `[Sensitive]` | Parameter | | Masks value as `***` in logs |
| `[return: Sensitive]` | Return value | | Masks return value as `***` in exit logs |
| `[NoLog]` | Parameter | | Excludes parameter from logs entirely |
| `[return: NoLog]` | Return value | | Excludes return value from exit logs |

### Assembly-level defaults

Set defaults for all `[Log]` methods in the assembly. Per-method properties override these.

```csharp
[assembly: ZibLogDefaults(
    EntryExitLevel = ZibLogLevel.Debug,
    ObjectLogging = ObjectLogMode.Json,
    MeasureElapsed = false)]
```

| Property | Default | Description |
|---|---|---|
| `EntryExitLevel` | `Information` | Default log level for entry/exit |
| `ExceptionLevel` | `Error` | Default log level for exceptions |
| `LogParameters` | `true` | Log parameter values |
| `LogReturnValue` | `true` | Log return value |
| `MeasureElapsed` | `true` | Measure elapsed time |
| `ObjectLogging` | `Destructure` | How complex objects are logged |

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
