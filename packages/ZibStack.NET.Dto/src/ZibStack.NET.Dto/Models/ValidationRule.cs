namespace ZibStack.NET.Dto;

internal enum ValidationRuleKind
{
    MinLength,
    MaxLength,
    StringLength,
    Range,
    Email,
    Url,
    Regex,
    NotEmpty,
    Phone,
}

internal sealed class ValidationRule
{
    public ValidationRuleKind Kind { get; }
    public int? IntParam1 { get; }
    public int? IntParam2 { get; }
    public double? DoubleParam1 { get; }
    public double? DoubleParam2 { get; }
    public string? StringParam { get; }
    public string? Message { get; }

    public ValidationRule(ValidationRuleKind kind, int? intParam1 = null, int? intParam2 = null,
        double? doubleParam1 = null, double? doubleParam2 = null, string? stringParam = null, string? message = null)
    {
        Kind = kind;
        IntParam1 = intParam1;
        IntParam2 = intParam2;
        DoubleParam1 = doubleParam1;
        DoubleParam2 = doubleParam2;
        StringParam = stringParam;
        Message = message;
    }
}
