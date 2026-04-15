using System;

namespace ZibStack.NET.TypeGen;

/// <summary>
/// Marks a class for code generation in one or more target languages / formats.
/// Without this attribute, a class is invisible to the generator — opt-in is the
/// default to avoid emitting types the developer didn't intend to expose.
///
/// <para>
/// Combine targets with bitwise OR. <see cref="OutputDir"/> is interpreted relative
/// to the project directory (<c>$MSBuildProjectDir</c>) — use <c>"../client/src/api"</c>
/// to write into a sibling frontend project. An absolute path is also accepted.
/// </para>
///
/// <para>
/// Per-class overrides (<see cref="TsNameAttribute"/>, <see cref="OpenApiSchemaNameAttribute"/>)
/// take precedence over global settings configured via
/// <see cref="ITypeGenConfigurator"/>. See the package docs for the full precedence chain.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
///                OutputDir = "../client/src/api")]
/// public class Order
/// {
///     public int Id { get; set; }
///     public string Customer { get; set; } = "";
///     [Sensitive] public string CreditCard { get; set; } = "";   // skipped in output
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum,
    Inherited = false, AllowMultiple = false)]
public sealed class GenerateTypesAttribute : Attribute
{
    /// <summary>
    /// Which output formats to produce. Required — passing
    /// <see cref="TypeTarget.None"/> is treated as a configuration error by the analyzer.
    /// </summary>
    public TypeTarget Targets { get; set; }

    /// <summary>
    /// Output directory, relative to the project directory or absolute. The actual
    /// file name follows each emitter's settings — TypeScript defaults to one file
    /// per class (<c>OrderModel.ts</c>), OpenAPI to a single document
    /// (<c>openapi.yaml</c>). Override either default via
    /// <see cref="ITypeGenConfigurator"/>.
    /// </summary>
    public string OutputDir { get; set; } = ".";

    public GenerateTypesAttribute() { }
    public GenerateTypesAttribute(TypeTarget targets) { Targets = targets; }
}
