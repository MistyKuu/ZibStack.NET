using System.Collections.Generic;

namespace ZibStack.NET.Utils;

internal sealed class IntersectTargetInfo
{
    public string TypeName { get; }
    public List<UtilsPropertyInfo> Properties { get; }

    public IntersectTargetInfo(string typeName, List<UtilsPropertyInfo> properties)
    {
        TypeName = typeName;
        Properties = properties;
    }
}

internal sealed class IntersectInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    public string TypeKeyword { get; }
    public List<IntersectTargetInfo> TargetTypes { get; }

    public IntersectInfo(string className, string? ns, string fullyQualifiedName, List<IntersectTargetInfo> targetTypes, string typeKeyword = "record")
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        TypeKeyword = typeKeyword;
        TargetTypes = targetTypes;
    }
}
