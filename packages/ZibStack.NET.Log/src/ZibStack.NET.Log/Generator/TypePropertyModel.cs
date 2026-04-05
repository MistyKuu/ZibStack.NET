using System;
using System.Collections.Generic;
using System.Linq;

namespace ZibStack.NET.Log.Generator;

internal sealed class TypePropertyModel : IEquatable<TypePropertyModel>
{
    public string Name { get; }
    public string Type { get; }
    public string FullyQualifiedType { get; }
    public bool IsSensitive { get; }
    public bool IsNoLog { get; }
    public bool IsComplexType { get; }

    /// <summary>
    /// If this property is a complex type, its own properties (for recursive serialization).
    /// Null if simple type or max depth reached.
    /// </summary>
    public IReadOnlyList<TypePropertyModel>? NestedProperties { get; }

    public TypePropertyModel(
        string name,
        string type,
        string fullyQualifiedType,
        bool isSensitive,
        bool isNoLog,
        bool isComplexType,
        IReadOnlyList<TypePropertyModel>? nestedProperties)
    {
        Name = name;
        Type = type;
        FullyQualifiedType = fullyQualifiedType;
        IsSensitive = isSensitive;
        IsNoLog = isNoLog;
        IsComplexType = isComplexType;
        NestedProperties = nestedProperties;
    }

    public bool Equals(TypePropertyModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && FullyQualifiedType == other.FullyQualifiedType
            && IsSensitive == other.IsSensitive
            && IsNoLog == other.IsNoLog;
    }

    public override bool Equals(object? obj) => Equals(obj as TypePropertyModel);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Name.GetHashCode();
            hash = hash * 31 + FullyQualifiedType.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// Holds property info for a complex type that needs custom serialization
/// (has at least one [Sensitive] or [NoLog] property, possibly nested).
/// </summary>
internal sealed class SanitizedTypeModel : IEquatable<SanitizedTypeModel>
{
    public string FullyQualifiedType { get; }
    public string SafeName { get; }
    public IReadOnlyList<TypePropertyModel> Properties { get; }

    public SanitizedTypeModel(string fullyQualifiedType, string safeName, IReadOnlyList<TypePropertyModel> properties)
    {
        FullyQualifiedType = fullyQualifiedType;
        SafeName = safeName;
        Properties = properties;
    }

    public bool Equals(SanitizedTypeModel? other)
    {
        if (other is null) return false;
        return FullyQualifiedType == other.FullyQualifiedType;
    }

    public override bool Equals(object? obj) => Equals(obj as SanitizedTypeModel);
    public override int GetHashCode() => FullyQualifiedType.GetHashCode();
}
