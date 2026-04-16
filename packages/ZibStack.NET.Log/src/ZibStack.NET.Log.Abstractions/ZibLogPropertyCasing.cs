namespace ZibStack.NET.Log;

/// <summary>
/// Casing convention for structured property names in interpolated log messages.
/// </summary>
public enum ZibLogPropertyCasing
{
    /// <summary>PascalCase: <c>userId</c> → <c>UserId</c>. Matches Serilog/Seq/Elastic convention. Default.</summary>
    PascalCase = 0,
    /// <summary>camelCase: <c>userId</c> stays <c>userId</c>. Matches the C# variable name as-is.</summary>
    CamelCase = 1,
}
