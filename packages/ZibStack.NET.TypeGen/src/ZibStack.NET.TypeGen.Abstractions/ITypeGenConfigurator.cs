namespace ZibStack.NET.TypeGen;

/// <summary>
/// Marker interface for a project-level configuration class that supplies global
/// settings to the TypeGen generator. Implement once per project; the generator
/// auto-discovers the type by interface and reads its property initializers as
/// declarative data via the Roslyn syntax tree.
///
/// <para>
/// <b>Compile-time data only.</b> The generator never invokes methods on this
/// class — it parses the syntax of property getters and constructor initializers
/// to extract values. This means:
/// </para>
/// <list type="bullet">
///   <item>Property getters must return a fresh <c>new TypeScriptSettings { ... }</c>
///         (or <c>OpenApiSettings { ... }</c>) initializer expression.</item>
///   <item>Field/property values inside the initializer must be literals, constants,
///         or <c>nameof(...)</c> expressions — anything dynamic (method calls,
///         conditionals) is invisible to the generator.</item>
///   <item>Multiple <see cref="ITypeGenConfigurator"/> implementations in one project
///         is an error (analyzer <c>TG0010</c>).</item>
/// </list>
///
/// <para>
/// For per-class / per-property overrides, use the attribute family
/// (<see cref="TsNameAttribute"/>, <see cref="OpenApiPropertyAttribute"/>, etc.) —
/// they take precedence over global settings configured here.
/// </para>
/// </summary>
/// <example>
/// <code>
/// public sealed class MyTypeGenConfig : ITypeGenConfigurator
/// {
///     public TypeScriptSettings TypeScript => new()
///     {
///         OutputDir = "../client/src/api",
///         FileLayout = TypeScriptFileLayout.SingleFile,
///         SingleFileName = "models.ts",
///         UseInterfaces = true,
///         PropertyNameStyle = NameStyle.CamelCase,
///         StripSuffixes = { "Dto", "Model" },
///     };
///
///     public OpenApiSettings OpenApi => new()
///     {
///         OutputPath = "../api/openapi.yaml",
///         Title = "Order Service",
///         Version = "2.1.0",
///         Description = "Public API for the order service.",
///     };
/// }
/// </code>
/// </example>
public interface ITypeGenConfigurator
{
    /// <summary>Global TypeScript emitter settings. Return <c>new TypeScriptSettings()</c> for defaults.</summary>
    TypeScriptSettings TypeScript { get; }

    /// <summary>Global OpenAPI emitter settings. Return <c>new OpenApiSettings()</c> for defaults.</summary>
    OpenApiSettings OpenApi { get; }
}
