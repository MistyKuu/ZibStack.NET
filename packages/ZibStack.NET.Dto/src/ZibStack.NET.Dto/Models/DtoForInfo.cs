using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class DtoForInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    public string TargetTypeName { get; }
    public string GenericParams { get; }
    public string TypeKeyword { get; }
    public List<DtoPropertyInfo> Properties { get; }
    public List<DtoClassInfo> AutoNestedDtos { get; } = new();

    public DtoForInfo(string className, string? ns, string fullyQualifiedName, string targetTypeName, List<DtoPropertyInfo> properties, string genericParams = "", string typeKeyword = "record")
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        TargetTypeName = targetTypeName;
        GenericParams = genericParams;
        TypeKeyword = typeKeyword;
        Properties = properties;
    }
}
