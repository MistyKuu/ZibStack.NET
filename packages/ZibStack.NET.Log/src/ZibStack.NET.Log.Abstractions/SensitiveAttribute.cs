namespace ZibStack.NET.Log;

/// <summary>
/// Marks a parameter or return value as sensitive. Its value will be masked as "***" in log output.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class SensitiveAttribute : Attribute
{
}
