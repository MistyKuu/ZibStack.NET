namespace ZibStack.NET.Log;

/// <summary>
/// Marks a class for ZibStack.Log source generation.
/// The generator will create interceptors for all methods decorated with <see cref="LogAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ZibLogAttribute : Attribute
{
    /// <summary>
    /// Name of the ILogger field to use. If null, the generator auto-detects the first ILogger/ILogger&lt;T&gt; field.
    /// </summary>
    public string? LoggerField { get; set; }
}
