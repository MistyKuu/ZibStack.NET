using ZibStack.NET.Aop;

namespace ZibStack.NET.Log;

/// <summary>
/// Casing convention for structured property names in interpolated log messages.
/// </summary>
public enum ZibLogPropertyCasing
{
    /// <summary>PascalCase: <c>userId</c> → <c>UserId</c>. Matches Serilog/Seq/Elastic convention. This is the default.</summary>
    PascalCase = 0,
    /// <summary>camelCase: <c>userId</c> stays <c>userId</c>. Matches the C# variable name as-is.</summary>
    CamelCase = 1,
}

/// <summary>
/// Sets default values for all <see cref="LogAttribute"/> instances in the assembly,
/// and controls interpolated-string logging conventions (property name casing).
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

    /// <summary>
    /// Casing convention for structured property names in <c>LogInformation($"...")</c> calls.
    /// Default: PascalCase (0) — <c>userId</c> becomes <c>UserId</c> in the log template.
    /// Set to CamelCase (1) to keep variable names as-is.
    /// </summary>
    public int PropertyNameCasing { get; set; } = 0;
}
