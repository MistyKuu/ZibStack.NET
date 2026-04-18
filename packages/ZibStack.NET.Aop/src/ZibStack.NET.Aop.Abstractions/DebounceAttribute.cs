using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that delays method execution until a quiet period elapses.
/// If the method is called again before <see cref="DelayMs"/> expires, the
/// previous pending call is cancelled and the timer resets.
///
/// <para>
/// Useful for search-as-you-type, auto-save, or any scenario where rapid
/// successive calls should collapse into a single execution after the caller
/// stops firing.
/// </para>
///
/// <para>
/// The debounce key is derived from the class name, method name, and argument
/// values — so calls with different arguments are debounced independently.
/// </para>
///
/// <para>
/// Only works on async methods (<c>Task</c>/<c>ValueTask</c> returning).
/// The built-in <see cref="DebounceHandler"/> is registered automatically by
/// <c>AddAop()</c>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Debounce(DelayMs = 300)]
/// public async Task OnSearchChangedAsync(string query) { ... }
///
/// [Debounce(DelayMs = 1000)]
/// public async Task AutoSaveAsync(Document doc) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(DebounceHandler))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DebounceAttribute : AspectAttribute
{
    /// <summary>
    /// Quiet period in milliseconds. The method executes only after no new calls
    /// arrive for this duration. Default: 300ms.
    /// </summary>
    public int DelayMs { get; set; } = 300;
}
