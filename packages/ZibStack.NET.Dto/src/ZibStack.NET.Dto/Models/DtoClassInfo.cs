using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal enum DtoKind { Create, Update, Combined }

internal sealed class DtoClassInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    public string RequestName { get; }
    public string GenericParams { get; }
    public DtoKind Kind { get; }
    public List<DtoPropertyInfo> Properties { get; }
    public string? CreateValidatorTypeName { get; }
    public string? UpdateValidatorTypeName { get; }
    public List<DtoClassInfo> AutoNestedDtos { get; } = new();

    /// <summary>
    /// Set by the pipeline when ZibStack.NET.TypeGen is referenced. Causes the
    /// generated record to carry <c>[GenerateTypes(TypeTarget.OpenApi)]</c> so
    /// TypeGen's OpenAPI emitter discovers it and the <c>$ref</c> from CRUD paths
    /// resolves against a real schema.
    /// </summary>
    public bool HasTypeGen { get; set; }

    public DtoClassInfo(string className, string? ns, string fullyQualifiedName, string requestName, DtoKind kind, List<DtoPropertyInfo> properties, string? createValidatorTypeName, string? updateValidatorTypeName, string genericParams = "")
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        RequestName = requestName;
        GenericParams = genericParams;
        Kind = kind;
        Properties = properties;
        CreateValidatorTypeName = createValidatorTypeName;
        UpdateValidatorTypeName = updateValidatorTypeName;
    }
}
