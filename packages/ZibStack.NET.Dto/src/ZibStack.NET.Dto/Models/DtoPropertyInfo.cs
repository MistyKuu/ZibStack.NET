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
    public List<string> ValidationAttributes { get; }
    public List<ValidationRule> ValidationRules { get; }
    public string? NestedCreateDtoName { get; set; }
    public string? NestedUpdateDtoName { get; set; }
    public string? FlattenEntityPath { get; set; }
    public bool IsFlattened => FlattenEntityPath is not null;

    /// <summary>
    /// Which DTO targets this property is excluded from (via [DtoIgnore(target)]).
    /// DtoTarget.All means excluded from everything.
    /// 0 means no ignoring.
    /// </summary>
    public int IgnoreTargets { get; }

    /// <summary>
    /// When non-zero, property is included ONLY in these targets (via [DtoOnly(target)]).
    /// 0 means "include in all targets" (default). Mutually exclusive with IgnoreTargets.
    /// </summary>
    public int OnlyTargets { get; }

    // DtoTarget flags: Create=1, Update=2, Response=4, Query=8, List=16
    public bool IsIgnoredFrom(int target) => IgnoreTargets != 0
        ? (IgnoreTargets & target) != 0
        : OnlyTargets != 0 && (OnlyTargets & target) == 0;

    /// <summary>
    /// True when the source property has no externally visible setter —
    /// <c>{ get; private set; }</c> or (after the getter-only filter) any
    /// property Roslyn reports as computed. Server owns the value: the Create
    /// and Update request DTOs both skip it, because a client-provided value
    /// would have nowhere to go when <c>ToEntity()</c>/<c>ApplyTo()</c> runs.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// True for <c>{ get; init; }</c> accessors — settable exactly once, at
    /// object construction. The Create DTO still accepts it (init-time value
    /// is the point), but Update DTO skips it: post-creation reassignment is
    /// what <c>init</c> forbids, and silently dropping writes there would be
    /// a footgun.
    /// </summary>
    public bool IsInitOnly { get; }

    public DtoPropertyInfo(string propertyName, string jsonName, string displayTypeName,
        bool isNullable, bool isRequired, bool isValueType,
        int ignoreTargets, int onlyTargets,
        string? sourcePropertyName = null,
        List<string>? validationAttributes = null, List<ValidationRule>? validationRules = null,
        bool isReadOnly = false, bool isInitOnly = false)
    {
        PropertyName = propertyName;
        SourcePropertyName = sourcePropertyName ?? propertyName;
        JsonName = jsonName;
        DisplayTypeName = displayTypeName;
        IsNullable = isNullable;
        IsRequired = isRequired;
        IsValueType = isValueType;
        IgnoreTargets = ignoreTargets;
        OnlyTargets = onlyTargets;
        ValidationAttributes = validationAttributes ?? new List<string>();
        ValidationRules = validationRules ?? new List<ValidationRule>();
        IsReadOnly = isReadOnly;
        IsInitOnly = isInitOnly;
    }
}
