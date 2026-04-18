using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that limits how often a method can execute. At most one
/// invocation is allowed per <see cref="IntervalMs"/> window. The first call
/// executes immediately; subsequent calls within the interval are dropped.
///
/// <para>
/// When <see cref="Trailing"/> is <c>true</c> (the default), the last
/// suppressed call is executed when the interval expires — ensuring the
/// final state is always processed.
/// </para>
///
/// <para>
/// The throttle key is derived from the class name, method name, and argument
/// values — so calls with different arguments are throttled independently.
/// </para>
///
/// <para>
/// Only works on async methods (<c>Task</c>/<c>ValueTask</c> returning).
/// The built-in <see cref="ThrottleHandler"/> is registered automatically by
/// <c>AddAop()</c>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Throttle(IntervalMs = 1000)]
/// public async Task SendNotificationAsync(string userId, string message) { ... }
///
/// [Throttle(IntervalMs = 5000, Trailing = false)]
/// public async Task RefreshDashboardAsync() { ... }
/// </code>
/// </example>
[AspectHandler(typeof(ThrottleHandler))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ThrottleAttribute : AspectAttribute
{
    /// <summary>
    /// Minimum interval between executions in milliseconds. Default: 1000ms.
    /// </summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>
    /// When <c>true</c>, the last suppressed call is executed after the interval
    /// expires. This ensures the final value is always processed. Default: <c>true</c>.
    /// </summary>
    public bool Trailing { get; set; } = true;
}
