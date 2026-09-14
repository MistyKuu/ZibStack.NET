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
