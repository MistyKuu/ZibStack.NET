using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

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
    /// Cancellation token source the generator wires up when the intercepted method
    /// has a <see cref="CancellationToken"/> parameter. Set BY THE GENERATOR (or stays
    /// null otherwise). Handlers that need to signal cooperative cancellation to the
    /// method body — e.g. <c>TimeoutHandler</c> calling
    /// <c>ctx.CancellationTokenSource.CancelAfter(...)</c> — read this property and
    /// only act when it's present. The generator emits a <c>using</c> around the call
    /// site so disposal happens regardless of how the call ends.
    ///
    /// <para>
    /// The token wired into the method's CT parameter is
    /// <c>CancellationTokenSource.CreateLinkedTokenSource(callerToken).Token</c>, so
    /// canceling the CTS here also cancels what the caller passed in (via the linked
    /// chain), and the method body sees the cancellation through its own awaits.
    /// </para>
    /// </summary>
    public CancellationTokenSource? CancellationTokenSource { get; set; }

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
