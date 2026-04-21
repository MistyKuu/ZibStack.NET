namespace ZibStack.NET.Aop;

/// <summary>
/// Marks a method or class for automatic logging. When applied to a class, all public
/// instance methods are logged. The source generator creates interceptors that log
/// method entry, exit, parameters, return values, and exceptions.
/// </summary>
/// <example>
/// <code>
/// // On a method:
/// [Log]
/// public Order GetOrder(int id) { ... }
///
/// // On a class (logs all public methods):
/// [Log]
/// public class OrderService { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class LogAttribute : AspectAttribute
{
    /// <summary>
    /// Log level for entry/exit messages. Use <see cref="ZibLogLevel"/> constants.
    /// Default: <see cref="ZibLogLevel.Information"/> (2).
    /// </summary>
    public int EntryExitLevel { get; set; } = 2;

    /// <summary>
    /// Log level used when the method throws an exception.
    /// Default: <see cref="ZibLogLevel.Error"/> (4).
    /// </summary>
    public int ExceptionLevel { get; set; } = 4;

    /// <summary>
    /// Whether to log parameter values on entry. Default: true.
    /// </summary>
    public bool LogParameters { get; set; } = true;

    /// <summary>
    /// Whether to log the return value on exit. Default: true.
    /// </summary>
    public bool LogReturnValue { get; set; } = true;

    /// <summary>
    /// Whether to measure and log elapsed time. Default: true.
    /// </summary>
    public bool MeasureElapsed { get; set; } = true;

    /// <summary>
    /// Custom entry log message template. Parameter names can be used as placeholders.
    /// </summary>
    public string? EntryMessage { get; set; }

    /// <summary>
    /// Custom exit log message template. Supports {ElapsedMs} and {Result} placeholders.
    /// </summary>
    public string? ExitMessage { get; set; }

    /// <summary>
    /// Custom exception log message template. Supports {ElapsedMs} placeholder.
    /// </summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>
    /// How complex objects are serialized in logs. Use <see cref="ObjectLogMode"/> constants.
    /// Default: <see cref="ObjectLogMode.Destructure"/> (1).
    /// </summary>
    public int ObjectLogging { get; set; } = 1;
}
