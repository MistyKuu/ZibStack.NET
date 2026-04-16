using System.Collections.Generic;

namespace ZibStack.NET.TypeGen;

/// <summary>
/// Naming convention applied to identifiers in emitted output (TypeScript property
/// names, JSON Schema field names, etc.).
/// </summary>
public enum NameStyle
{
    /// <summary>Pass through C# names verbatim (default for type names).</summary>
    AsIs,

    /// <summary><c>orderId</c> — first letter lowercase, rest as-is. Default for TS properties.</summary>
    CamelCase,

    /// <summary><c>order_id</c> — words separated by underscore, all lowercase.</summary>
    SnakeCase,

    /// <summary><c>OrderId</c> — first letter uppercase, rest as-is.</summary>
    PascalCase,
}

/// <summary>
/// Layout strategy for TypeScript output files.
/// </summary>
public enum TypeScriptFileLayout
{
    /// <summary>One <c>.ts</c> file per source class — file name follows the emitted type name.</summary>
    FilePerClass,

    /// <summary>
    /// All emitted types concatenated into a single file. Useful for tree-shaken
    /// frontend bundlers that prefer one entrypoint.
    /// </summary>
    SingleFile,
}

/// <summary>
/// TypeScript emitter settings. All fields have defaults; configure via
/// <c>typegen.config.json</c> or <see cref="ITypeGenConfigurator.TypeScript"/>.
/// </summary>
public sealed class TypeScriptSettings
{
    /// <summary>
    /// Output directory, relative to the project or absolute. When the
    /// <see cref="GenerateTypesAttribute.OutputDir"/> on a class is set, that wins
    /// per-class. This setting is the global fallback for classes that don't specify.
    /// </summary>
    public string? OutputDir { get; set; }

    /// <summary>
    /// File name for <see cref="TypeScriptFileLayout.SingleFile"/> mode. Ignored
    /// in <see cref="TypeScriptFileLayout.FilePerClass"/>. Default <c>"models.ts"</c>.
    /// </summary>
    public string SingleFileName { get; set; } = "models.ts";

    /// <summary>Default <see cref="TypeScriptFileLayout.FilePerClass"/>.</summary>
    public TypeScriptFileLayout FileLayout { get; set; } = TypeScriptFileLayout.FilePerClass;

    /// <summary>
    /// Emit reference types as TypeScript <c>interface</c>s when <c>true</c>
    /// (idiomatic for data shapes, allows declaration merging) or <c>type</c> aliases
    /// when <c>false</c> (more flexible — supports unions, intersections at top level).
    /// Default <c>true</c>.
    /// </summary>
    public bool UseInterfaces { get; set; } = true;

    /// <summary>Default <see cref="NameStyle.CamelCase"/> — TS convention.</summary>
    public NameStyle PropertyNameStyle { get; set; } = NameStyle.CamelCase;

    /// <summary>Default <see cref="NameStyle.AsIs"/> — keep C# class names.</summary>
    public NameStyle TypeNameStyle { get; set; } = NameStyle.AsIs;

    /// <summary>
    /// Class-name suffixes to strip from emitted type names, in order. E.g.
    /// <c>["Dto", "Model"]</c> turns <c>OrderDto</c> into <c>Order</c> and
    /// <c>UserModel</c> into <c>User</c>. Empty by default — opt-in.
    /// </summary>
    public IList<string> StripSuffixes { get; } = new List<string>();

    /// <summary>
    /// Emit a <c>// @generated — do not edit</c> banner at the top of every output
    /// file. Default <c>true</c> to deter manual edits that would be overwritten.
    /// </summary>
    public bool EmitGeneratedBanner { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, TypeGen discovers C# interfaces implemented by emitted
    /// classes (and emits them as TS interfaces with <c>extends</c> / OpenAPI
    /// schemas composed into <c>allOf</c>). Default <c>false</c> — interfaces
    /// stay invisible to the generator, matching the original behaviour. Flip on
    /// when your DTOs carry shared contracts via interfaces (<c>IAuditable</c>,
    /// <c>IHasId</c>) and you want those contracts to surface in the emitted
    /// types. The flag controls discovery across every target (TS, OpenAPI,
    /// Python) — it lives on <see cref="TypeScriptSettings"/> because TS is the
    /// most natural home for the interface concept; OpenAPI and Python pick up
    /// the same flag via <see cref="GlobalSettings"/>.
    /// </summary>
    public bool EmitInterfaces { get; set; } = false;
}

/// <summary>
/// Layout strategy for Python output files.
/// </summary>
public enum PythonFileLayout
{
    /// <summary>One <c>.py</c> file per source class.</summary>
    FilePerClass,

    /// <summary>All models concatenated into one file (default <c>models.py</c>).</summary>
    SingleFile,
}

/// <summary>
/// Python emitter settings. Targets Pydantic v2 — if you want plain <c>dataclass</c>
/// instead, set <see cref="Style"/>. All settings have sensible defaults; override
/// via <c>ITypeGenConfigurator</c>.
/// </summary>
public sealed class PythonSettings
{
    /// <summary>Output directory; relative to the project or absolute.</summary>
    public string? OutputDir { get; set; }

    /// <summary>File layout — one per class (default) or single bundled <c>models.py</c>.</summary>
    public PythonFileLayout FileLayout { get; set; } = PythonFileLayout.FilePerClass;

    /// <summary>File name when <see cref="FileLayout"/> is <see cref="PythonFileLayout.SingleFile"/>.</summary>
    public string SingleFileName { get; set; } = "models.py";

    /// <summary>
    /// Emit style. <see cref="PythonStyle.Pydantic"/> (default) produces
    /// <c>BaseModel</c> subclasses with validation; <see cref="PythonStyle.Dataclass"/>
    /// produces stdlib <c>@dataclass</c>es (no runtime deps).
    /// </summary>
    public PythonStyle Style { get; set; } = PythonStyle.Pydantic;

    /// <summary>
    /// Convert property names to <c>snake_case</c> (PEP 8) when emitting. Default
    /// <c>true</c> — with Pydantic, aliasing preserves the original PascalCase
    /// on JSON parsing via <c>Field(alias=...)</c>.
    /// </summary>
    public bool SnakeCaseProperties { get; set; } = true;

    /// <summary>Emit the standard <c># @generated</c> banner at the top of each file.</summary>
    public bool EmitGeneratedBanner { get; set; } = true;
}

/// <summary>Python emission style.</summary>
public enum PythonStyle
{
    /// <summary>Pydantic v2 <c>BaseModel</c> — JSON parse/serialize + validation.</summary>
    Pydantic,

    /// <summary>Plain <c>@dataclass</c> — no runtime dependencies, no validation.</summary>
    Dataclass,
}

/// <summary>
/// OpenAPI emitter settings. Default target is OpenAPI 3.0.3 — see
/// <see cref="OpenApiVersion"/>.
/// </summary>
public sealed class OpenApiSettings
{
    /// <summary>
    /// Full output path (directory + file name), relative to the project or absolute.
    /// Default <c>"openapi.yaml"</c> in the project directory. The file extension
    /// determines the format — <c>.yaml</c>/<c>.yml</c> emits YAML, <c>.json</c>
    /// emits JSON.
    /// </summary>
    public string OutputPath { get; set; } = "openapi.yaml";

    /// <summary>OpenAPI <c>info.title</c> — required by spec. Default <c>"API"</c>.</summary>
    public string Title { get; set; } = "API";

    /// <summary>OpenAPI <c>info.version</c>. Default <c>"1.0.0"</c>.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Optional <c>info.description</c>.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// OpenAPI version emitted in the <c>openapi</c> field. Default <c>"3.0.3"</c>
    /// for broadest tooling compatibility (Swashbuckle, NSwag codegen,
    /// Microsoft.OpenApi.Readers 1.6.x don't yet support 3.1). Override if you
    /// target readers that do.
    /// </summary>
    public string OpenApiVersion { get; set; } = "3.0.3";
}
