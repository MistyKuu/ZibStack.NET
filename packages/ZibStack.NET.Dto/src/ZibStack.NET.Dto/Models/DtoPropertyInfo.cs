using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class DtoPropertyInfo
{
    public string PropertyName { get; }
    public string SourcePropertyName { get; }
    public string JsonName { get; }
    public string DisplayTypeName { get; }
    public bool IsNullable { get; }
    public bool IsRequired { get; }
    public bool IsValueType { get; }
    public bool IsCreateOnly { get; }
    public bool IsUpdateOnly { get; }
    public bool IsImmutable { get; }
    public List<string> ValidationAttributes { get; }
    public List<ValidationRule> ValidationRules { get; }
    public string? NestedCreateDtoName { get; set; }
    public string? NestedUpdateDtoName { get; set; }
    public string? FlattenEntityPath { get; set; }
    public bool IsFlattened => FlattenEntityPath is not null;

    public DtoPropertyInfo(string propertyName, string jsonName, string displayTypeName, bool isNullable, bool isRequired, bool isValueType, bool isCreateOnly, bool isUpdateOnly, bool isImmutable = false, string? sourcePropertyName = null, List<string>? validationAttributes = null, List<ValidationRule>? validationRules = null)
    {
        PropertyName = propertyName;
        SourcePropertyName = sourcePropertyName ?? propertyName;
        JsonName = jsonName;
        DisplayTypeName = displayTypeName;
        IsNullable = isNullable;
        IsRequired = isRequired;
        IsValueType = isValueType;
        IsCreateOnly = isCreateOnly;
        IsUpdateOnly = isUpdateOnly;
        IsImmutable = isImmutable;
        ValidationAttributes = validationAttributes ?? new List<string>();
        ValidationRules = validationRules ?? new List<ValidationRule>();
    }
}
