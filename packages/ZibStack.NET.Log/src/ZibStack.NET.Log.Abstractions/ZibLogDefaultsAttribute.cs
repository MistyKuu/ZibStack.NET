using ZibStack.NET.Aop;

namespace ZibStack.NET.Log;

/// <summary>
/// Sets default values for all <see cref="LogAttribute"/> instances in the assembly.
/// Per-method [Log] properties override these defaults.
/// </summary>
/// <example>
/// <code>
/// // Set defaults for the entire assembly:
/// [assembly: ZibLogDefaults(EntryExitLevel = ZibLogLevel.Debug, MeasureElapsed = false, ObjectLogging = ObjectLogMode.Json)]
///
/// // This method uses assembly defaults (Debug level, no stopwatch, JSON):
/// [Log]
/// public Order GetOrder(int id) { ... }
///
/// // This method overrides — Warning level, but still no stopwatch + JSON from defaults:
/// [Log(EntryExitLevel = ZibLogLevel.Warning)]
/// public void DeleteOrder(int id) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ZibLogDefaultsAttribute : Attribute
{
    /// <summary>Default log level for entry/exit. Default: Information (2).</summary>
    public int EntryExitLevel { get; set; } = -1;

    /// <summary>Default log level for exceptions. Default: Error (4).</summary>
    public int ExceptionLevel { get; set; } = -1;

    /// <summary>Default for logging parameters. Default: true.</summary>
    public bool LogParameters { get; set; } = true;

    /// <summary>Default for logging return value. Default: true.</summary>
    public bool LogReturnValue { get; set; } = true;

    /// <summary>Default for measuring elapsed time. Default: true.</summary>
    public bool MeasureElapsed { get; set; } = true;

    /// <summary>Default object logging mode. Default: Destructure (1).</summary>
    public int ObjectLogging { get; set; } = -1;
}
