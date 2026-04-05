using System;
using System.Collections.Generic;
using System.Text;

namespace ZibStack.NET.Aop;

/// <summary>
/// Context passed to <see cref="IAspectHandler"/> methods. Contains method info,
/// parameters (with sensitivity metadata), return value, and elapsed time.
/// </summary>
public sealed class AspectContext
{
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public IReadOnlyList<AspectParameterInfo> Parameters { get; set; } = Array.Empty<AspectParameterInfo>();
    public object? ReturnValue { get; set; }
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Shared data bag for passing state between aspects on the same call.
    /// </summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    /// <summary>
    /// Formats parameters respecting [Sensitive] (masked as ***) and [NoLog] (excluded).
    /// </summary>
    public string FormatParameters()
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var p in Parameters)
        {
            if (p.IsNoLog) continue;
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(p.Name);
            sb.Append(": ");
            sb.Append(p.IsSensitive ? "***" : (p.Value?.ToString() ?? "null"));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Describes a method parameter with sensitivity metadata.
/// </summary>
public sealed class AspectParameterInfo
{
    public string Name { get; set; } = "";
    public object? Value { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsNoLog { get; set; }
}
