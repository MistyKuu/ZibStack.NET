namespace ZibStack.NET.Aop;

/// <summary>
/// Controls how complex objects are serialized in log output.
/// </summary>
public static class ObjectLogMode
{
    /// <summary>Uses object.ToString().</summary>
    public new const int ToString = 0;

    /// <summary>Serilog-style destructuring ({@param}). Default.</summary>
    public const int Destructure = 1;

    /// <summary>JSON serialization via System.Text.Json.</summary>
    public const int Json = 2;
}
