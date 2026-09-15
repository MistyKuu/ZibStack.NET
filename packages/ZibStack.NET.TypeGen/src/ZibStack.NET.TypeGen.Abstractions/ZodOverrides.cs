using System;

namespace ZibStack.NET.TypeGen;

/// <summary>
/// Overrides the Zod string validator emitted for a property. This is useful for
/// JavaScript-specific wire formats that do not have a DataAnnotations equivalent.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ZodFormatAttribute : Attribute
{
    public ZodStringFormat Format { get; }

    /// <summary>Expected NanoID length. Used only with <see cref="ZodStringFormat.NanoId"/>.</summary>
    public int Length { get; set; }

    public ZodFormatAttribute(ZodStringFormat format) => Format = format;
}

/// <summary>
/// Replaces the inferred Zod validator for a property with a user-supplied Zod
/// schema expression. Use <see cref="ImportFrom"/> when the expression references
/// named exports from another module, such as <c>GeoJSONPointSchema</c> from
/// <c>zod-geojson</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ZodSchemaAttribute : Attribute
{
    /// <summary>Zod schema expression emitted verbatim for the property.</summary>
    public string SchemaExpression { get; }

    /// <summary>Optional module specifier supplying named symbols used by the expression.</summary>
    public string? ImportFrom { get; set; }

    public ZodSchemaAttribute(string schemaExpression) => SchemaExpression = schemaExpression;
}
