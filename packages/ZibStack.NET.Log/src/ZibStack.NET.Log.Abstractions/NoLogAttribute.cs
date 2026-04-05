namespace ZibStack.NET.Log;

/// <summary>
/// Excludes a parameter or return value from being logged entirely.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class NoLogAttribute : Attribute
{
}
