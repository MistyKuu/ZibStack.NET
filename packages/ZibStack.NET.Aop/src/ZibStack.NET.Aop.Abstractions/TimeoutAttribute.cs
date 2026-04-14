using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that enforces a maximum execution time on an async method.
/// If the method does not complete within <see cref="TimeoutMs"/> milliseconds,
/// a <see cref="TimeoutException"/> is thrown.
///
/// <para>
/// This aspect uses <see cref="IAsyncAroundAspectHandler"/> and only works on
/// async methods (<c>Task</c>/<c>ValueTask</c> returning). For synchronous methods,
/// use a custom handler instead.
/// </para>
///
/// <para>
/// The built-in <see cref="TimeoutHandler"/> is registered automatically by
/// <c>AddAop()</c>. You do not need to register it by hand.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Timeout(TimeoutMs = 5000)]
/// public async Task&lt;ExternalData&gt; CallExternalApiAsync(string endpoint) { ... }
///
/// [Timeout(TimeoutMs = 3000)]
/// public async Task&lt;Report&gt; GenerateReportAsync(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(TimeoutHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TimeoutAttribute : AspectAttribute
{
    /// <summary>
    /// Maximum allowed execution time in milliseconds. Default: 30000 (30 seconds).
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;
}
