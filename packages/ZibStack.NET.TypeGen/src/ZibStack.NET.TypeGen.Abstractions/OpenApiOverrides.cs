using System;

namespace ZibStack.NET.TypeGen;

/// <summary>
/// Overrides the emitted schema name for a class in OpenAPI's
/// <c>components/schemas</c> section. Without this attribute, the C# class name is used.
/// Useful for versioning (<c>OrderV1</c> / <c>OrderV2</c>) or namespace deduping.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum,
    Inherited = false, AllowMultiple = false)]
public sealed class OpenApiSchemaNameAttribute : Attribute
{
    public string Name { get; }
    public OpenApiSchemaNameAttribute(string name) => Name = name;
}

/// <summary>
/// Sets OpenAPI-specific metadata on a property — <c>format</c>, <c>example</c>,
/// <c>description</c>, <c>nullable</c> override, etc. Each field is optional;
/// omitted fields fall back to the analyzer's inferred values.
///
/// <para>
/// This attribute is purely additive — it doesn't change the inferred
/// <c>type</c>/<c>items</c>/<c>$ref</c> structure, only annotates it.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [OpenApiProperty(Format = "uri", Example = "https://example.com/order/42")]
/// public string CallbackUrl { get; set; } = "";
///
/// [OpenApiProperty(Description = "Customer's stable ID across systems.")]
/// public Guid CustomerId { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false, AllowMultiple = false)]
public sealed class OpenApiPropertyAttribute : Attribute
{
    /// <summary>
    /// OpenAPI <c>format</c> hint — <c>"uri"</c>, <c>"email"</c>, <c>"uuid"</c>,
    /// <c>"date-time"</c>, <c>"int64"</c>, <c>"binary"</c>, etc.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Example value emitted under the schema's <c>example</c> field. Strings are
    /// quoted as JSON strings; numbers and booleans pass through.
    /// </summary>
    public object? Example { get; set; }

    /// <summary>
    /// Free-form description emitted under the schema's <c>description</c> field.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional explicit nullability override. When unset, the analyzer infers
    /// nullability from the C# type (<c>string?</c>, <c>int?</c>, NRT context).
    /// </summary>
    public bool? Nullable { get; set; }
}

/// <summary>
/// Excludes the annotated class or property from OpenAPI output.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property |
                AttributeTargets.Field | AttributeTargets.Enum,
    Inherited = false, AllowMultiple = false)]
public sealed class OpenApiIgnoreAttribute : Attribute
{
}
