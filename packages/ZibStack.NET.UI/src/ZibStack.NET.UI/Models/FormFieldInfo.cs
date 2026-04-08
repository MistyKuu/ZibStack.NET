using System.Collections.Generic;

namespace ZibStack.NET.UI;

internal sealed class FormFieldInfo
{
    public string PropertyName { get; }
    public string JsonName { get; }
    public string TypeName { get; }
    public string FieldType { get; }
    public string UiHint { get; set; }
    public string? Label { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public int Order { get; set; }
    public string? Group { get; set; }
    public bool IsHidden { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsRequired { get; set; }
    public bool IsCreateOnly { get; set; }
    public bool IsUpdateOnly { get; set; }
    public bool IsNullable { get; set; }
    public bool IsEnum { get; set; }
    public string? EnumTypeFqn { get; set; }
    public List<SelectOptionInfo> Options { get; } = new List<SelectOptionInfo>();
    public ConditionalInfo? Conditional { get; set; }
    public List<ValidationRuleInfo> ValidationRules { get; } = new List<ValidationRuleInfo>();
    public Dictionary<string, string> Props { get; } = new Dictionary<string, string>();

    public FormFieldInfo(string propertyName, string jsonName, string typeName, string fieldType, string uiHint)
    {
        PropertyName = propertyName;
        JsonName = jsonName;
        TypeName = typeName;
        FieldType = fieldType;
        UiHint = uiHint;
    }
}

internal sealed class SelectOptionInfo
{
    public string Value { get; }
    public string Label { get; }

    public SelectOptionInfo(string value, string label)
    {
        Value = value;
        Label = label;
    }
}

internal sealed class ConditionalInfo
{
    public string FieldName { get; }
    public string Value { get; }
    public string Operator { get; }

    public ConditionalInfo(string fieldName, string value, string op = "equals")
    {
        FieldName = fieldName;
        Value = value;
        Operator = op;
    }
}

internal sealed class ValidationRuleInfo
{
    public string Kind { get; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public string? Pattern { get; set; }

    public ValidationRuleInfo(string kind)
    {
        Kind = kind;
    }
}
