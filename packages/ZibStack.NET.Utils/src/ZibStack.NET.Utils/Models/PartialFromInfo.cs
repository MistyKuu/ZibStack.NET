using System.Collections.Generic;

namespace ZibStack.NET.Utils;

internal sealed class PartialFromInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    public string TargetTypeName { get; }
    public string TypeKeyword { get; }
    public List<UtilsPropertyInfo> Properties { get; }

    public PartialFromInfo(string className, string? ns, string fullyQualifiedName, string targetTypeName, List<UtilsPropertyInfo> properties, string typeKeyword = "record")
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        TargetTypeName = targetTypeName;
        TypeKeyword = typeKeyword;
        Properties = properties;
    }
}
