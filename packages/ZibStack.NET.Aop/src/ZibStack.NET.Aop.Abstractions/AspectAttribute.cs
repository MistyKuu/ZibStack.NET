namespace ZibStack.NET.Aop;

/// <summary>
/// Base attribute for all aspects. Derive from this to create custom aspect attributes.
/// The source generator detects all attributes inheriting from <see cref="AspectAttribute"/>
/// and generates interceptors for decorated methods.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public abstract class AspectAttribute : Attribute
{
    /// <summary>
    /// Execution order. Lower numbers run first (outermost).
    /// OnBefore runs in ascending order, OnAfter/OnException in descending order.
    /// Default: 0.
    /// </summary>
    public int Order { get; set; }
}
