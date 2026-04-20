using System.Collections.Generic;

namespace ZibStack.NET.Validation;

// ─── Models ───────────────────────────────────────────────────────────

internal enum ValidationRuleKind
{
    Required,
    MinLength,
    MaxLength,
    Range,
    Email,
    Url,
    Match,
    NotEmpty,
    In,
    NotIn,
    CreditCard,
    Phone,
}

internal sealed class ValidationRule
{
    public ValidationRuleKind Kind { get; }
    public string? CustomMessage { get; }
    public double MinValue { get; }
    public double MaxValue { get; }
    public string? Pattern { get; }
    public string[]? AllowedValues { get; }

    public ValidationRule(ValidationRuleKind kind, string? customMessage = null, double minValue = 0, double maxValue = 0, string? pattern = null, string[]? allowedValues = null)
    {
        Kind = kind;
        CustomMessage = customMessage;
        MinValue = minValue;
        MaxValue = maxValue;
        Pattern = pattern;
        AllowedValues = allowedValues;
    }
}

internal sealed class PropertyValidationInfo
{
    public string PropertyName { get; }
    public string TypeName { get; }
    public bool IsNullableRef { get; }
    public bool IsValueType { get; }
    public List<ValidationRule> Rules { get; }

    public PropertyValidationInfo(string propertyName, string typeName, bool isNullableRef, bool isValueType, List<ValidationRule> rules)
    {
        PropertyName = propertyName;
        TypeName = typeName;
        IsNullableRef = isNullableRef;
        IsValueType = isValueType;
        Rules = rules;
    }
}

internal sealed class ValidationInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string HintName { get; }
    public bool IsRecord { get; }
    public bool IsPartial { get; }
    public List<PropertyValidationInfo> Properties { get; }

    public List<CrossFieldRule> CrossFieldRules { get; } = new();
    public List<NestedValidatableInfo> NestedProperties { get; } = new();
    public List<ConditionalRule> ConditionalRules { get; } = new();

    public ValidationInfo(string className, string? ns, string hintName, bool isRecord, List<PropertyValidationInfo> properties, bool isPartial = false)
    {
        ClassName = className;
        Namespace = ns;
        HintName = hintName;
        IsRecord = isRecord;
        IsPartial = isPartial;
        Properties = properties;
    }
}

internal enum CrossFieldRuleKind
{
    /// <summary>Free-form expression: b.Rule(x => x.End > x.Start, "msg")</summary>
    Expression,
    /// <summary>Property comparison: b.Property(x => x.End).GreaterThan(x => x.Start)</summary>
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    EqualTo,
    NotEqualTo,
}

internal sealed class NestedValidatableInfo
{
    public string PropertyName { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsCollection { get; set; }
}

internal sealed class CrossFieldRule
{
    public CrossFieldRuleKind Kind { get; set; }
    public string Message { get; set; } = "";
    /// <summary>For Expression kind: the raw C# expression text (e.g. "EndDate > StartDate").</summary>
    public string? ExpressionText { get; set; }
    /// <summary>For comparison kinds: left property name.</summary>
    public string? LeftProperty { get; set; }
    /// <summary>For comparison kinds: right property name.</summary>
    public string? RightProperty { get; set; }
}

internal sealed class ConditionalRule
{
    /// <summary>The condition expression text (with parameter stripped, e.g. "RequiresShipping").</summary>
    public string ConditionExpression { get; set; } = "";
    /// <summary>Inner rules to emit when condition is true.</summary>
    public List<CrossFieldRule> InnerRules { get; set; } = new();
}
