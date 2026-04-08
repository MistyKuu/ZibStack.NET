namespace ZibStack.NET.Query;

/// <summary>Filter comparison operators (Gridify-compatible).</summary>
public enum FilterOperator
{
    /// <summary><c>=</c> — equals</summary>
    Equals,
    /// <summary><c>!=</c> — not equals</summary>
    NotEquals,
    /// <summary><c>&lt;</c> — less than</summary>
    LessThan,
    /// <summary><c>&gt;</c> — greater than</summary>
    GreaterThan,
    /// <summary><c>&lt;=</c> — less than or equal</summary>
    LessThanOrEqual,
    /// <summary><c>&gt;=</c> — greater than or equal</summary>
    GreaterThanOrEqual,
    /// <summary><c>=*</c> — contains (like)</summary>
    Contains,
    /// <summary><c>!*</c> — not contains</summary>
    NotContains,
    /// <summary><c>^</c> — starts with</summary>
    StartsWith,
    /// <summary><c>!^</c> — not starts with</summary>
    NotStartsWith,
    /// <summary><c>$</c> — ends with</summary>
    EndsWith,
    /// <summary><c>!$</c> — not ends with</summary>
    NotEndsWith,
}
