using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Project-wide fluent configuration for the [Log] aspect.
///
/// <para>
/// Implement this interface in a sealed class; the AOP source
/// generator discovers implementations in your compilation and parses the
/// <see cref="Configure"/> method body as a compile-time DSL. The method is
/// <b>never invoked at runtime</b> — only its syntax is read. All arguments
/// must be literal expressions or constants.
/// </para>
///
/// <example>
/// <code>
/// public sealed class LogConfig : ILogConfigurator
/// {
///     public void Configure(ILogBuilder b)
///     {
///         b.Defaults(d =>
///         {
///             d.EntryExitLevel = ZibLogLevel.Debug;
///             d.MeasureElapsed = false;
///             d.ObjectLogging = ObjectLogMode.Json;
///         });
///         b.Interpolation(i =>
///         {
///             i.PropertyNameCasing = ZibLogPropertyCasing.CamelCase;
///         });
///     }
/// }
/// </code>
/// </example>
/// </summary>
public interface ILogConfigurator
{
    void Configure(ILogBuilder b);
}

/// <summary>Fluent builder. Chain sections; per-method <c>[Log(...)]</c> overrides still win.</summary>
public interface ILogBuilder
{
    /// <summary>Project-wide defaults merged into every <c>[Log]</c> call site.</summary>
    ILogBuilder Defaults(Action<LogDefaults> configure);

    /// <summary>Interpolated-string logging conventions (property name casing etc.).</summary>
    ILogBuilder Interpolation(Action<LogInterpolation> configure);
}

/// <summary>
/// Default values injected into every <c>[Log]</c> aspect, unless the attribute
/// explicitly overrides the same property. <c>-1</c> means "use the hard-coded
/// generator default".
/// </summary>
public sealed class LogDefaults
{
    /// <summary>Default log level for entry/exit. Use <see cref="ZibLogLevel"/>. Generator default: Information (2).</summary>
    public int EntryExitLevel { get; set; } = -1;

    /// <summary>Default log level for exceptions. Use <see cref="ZibLogLevel"/>. Generator default: Error (4).</summary>
    public int ExceptionLevel { get; set; } = -1;

    /// <summary>Default for logging parameters. Generator default: true.</summary>
    public bool LogParameters { get; set; } = true;

    /// <summary>Default for logging return value. Generator default: true.</summary>
    public bool LogReturnValue { get; set; } = true;

    /// <summary>Default for measuring elapsed time with a Stopwatch. Generator default: true.</summary>
    public bool MeasureElapsed { get; set; } = true;

    /// <summary>Default object logging mode. Use <see cref="ObjectLogMode"/>. Generator default: Destructure (1).</summary>
    public int ObjectLogging { get; set; } = -1;
}

/// <summary>Settings for <c>logger.LogInformation($"...")</c> interpolated-string handling.</summary>
public sealed class LogInterpolation
{
    /// <summary>
    /// Casing convention for structured property names in interpolated log messages.
    /// Use <see cref="ZibLogPropertyCasing"/>. Generator default: PascalCase (0).
    /// </summary>
    public int PropertyNameCasing { get; set; } = 0;
}
