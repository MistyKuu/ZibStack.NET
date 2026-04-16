using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZibStack.NET.TypeGen.Generator;

// ── Mirror types ────────────────────────────────────────────────────────────
//
// These mirror the user-facing types in ZibStack.NET.TypeGen.Abstractions
// (TypeTarget, NameStyle, TypeScriptSettings, OpenApiSettings, etc.). The
// duplication is intentional: Roslyn analyzers run in a load context that does
// NOT auto-resolve dependency assemblies, so anything the generator
// instantiates at code-generation time must live in its own DLL. The user
// continues to write `[GenerateTypes(Targets = TypeTarget.TypeScript)]` against
// the public Abstractions API; the parser reads those values as ints/strings
// from the AttributeData (no Abstractions.dll needed).

[System.Flags]
internal enum TypeTarget
{
    None = 0,
    TypeScript = 1 << 0,
    OpenApi = 1 << 1,
    Python = 1 << 2,
}

/// <summary>
/// Mirror of <c>ZibStack.NET.Dto.CrudOperations</c> — same numeric layout so we can
/// cast directly from the int read off the attribute. Kept in this assembly so
/// TypeGen has zero runtime dependency on the Dto package.
/// </summary>
[System.Flags]
internal enum CrudOperations
{
    None = 0,
    GetById = 1,
    GetList = 2,
    Create = 4,
    Update = 8,
    Delete = 16,
    BulkCreate = 32,
    BulkDelete = 64,
    All = GetById | GetList | Create | Update | Delete,
}

/// <summary>
/// Subset of <c>[CrudApi]</c> the OpenAPI emitter cares about: route + key
/// property + which operations to emit. Authorization, ApiStyle, etc. are out
/// of MVP scope.
/// </summary>
internal sealed class CrudApiInfo
{
    /// <summary>Explicit route override; when set, wins over conventional path.</summary>
    public string? Route { get; set; }

    /// <summary>Optional segment between <c>api/</c> and the pluralized class name.</summary>
    public string? RoutePrefix { get; set; }

    /// <summary>Path parameter name on operations targeting a single record.</summary>
    public string KeyProperty { get; set; } = "Id";

    /// <summary>Bitmask of operations to emit (GET-by-id, list, create, update, delete, bulk*).</summary>
    public CrudOperations Operations { get; set; } = CrudOperations.All;
}

internal enum NameStyle { AsIs, CamelCase, SnakeCase, KebabCase, PascalCase }
internal enum TypeScriptFileLayout { FilePerClass, SingleFile }
internal enum PythonFileLayout { FilePerClass, SingleFile }
internal enum PythonStyle { Pydantic, Dataclass }

internal sealed class PythonSettings
{
    public string? OutputDir { get; set; }
    public PythonFileLayout FileLayout { get; set; } = PythonFileLayout.FilePerClass;
    public string SingleFileName { get; set; } = "models.py";
    public PythonStyle Style { get; set; } = PythonStyle.Pydantic;
    public bool SnakeCaseProperties { get; set; } = true;
    public bool EmitGeneratedBanner { get; set; } = true;
}

internal sealed class TypeScriptSettings
{
    public string? OutputDir { get; set; }
    public string SingleFileName { get; set; } = "models.ts";
    public TypeScriptFileLayout FileLayout { get; set; } = TypeScriptFileLayout.FilePerClass;
    public bool UseInterfaces { get; set; } = true;
    public NameStyle PropertyNameStyle { get; set; } = NameStyle.CamelCase;
    public NameStyle TypeNameStyle { get; set; } = NameStyle.AsIs;
    public IList<string> StripSuffixes { get; } = new List<string>();
    public bool EmitGeneratedBanner { get; set; } = true;
}

internal sealed class OpenApiSettings
{
    public string OutputPath { get; set; } = "openapi.yaml";
    public string Title { get; set; } = "API";
    public string Version { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public string OpenApiVersion { get; set; } = "3.0.3";
}

/// <summary>
/// Language-agnostic schema model the analyzer builds from the source's
/// <c>[GenerateTypes]</c> classes. Each emitter (TypeScript, OpenAPI) consumes
/// the same model and projects it to its target format.
///
/// <para>
/// Mutable on purpose — internal pipeline phases populate it incrementally
/// (collect → resolve overrides → resolve type references → emit). External
/// hook APIs (deferred — see project_typegen_backlog memory) would also mutate
/// here.
/// </para>
/// </summary>
internal sealed class SchemaModel
{
    public List<SchemaClass> Classes { get; } = new();
    public List<SchemaEnum> Enums { get; } = new();
}

internal sealed class SchemaClass
{
    /// <summary>Originating C# fully-qualified name (used for type-reference lookups).</summary>
    public string CSharpFullName { get; set; } = "";

    /// <summary>Class name as the developer wrote it.</summary>
    public string SourceName { get; set; } = "";

    /// <summary>
    /// Default emitted name (used when no per-target override applies). Computed by
    /// applying global <see cref="TypeScriptSettings.StripSuffixes"/> + transforms.
    /// Each emitter may further override via per-class attributes.
    /// </summary>
    public string EmittedName { get; set; } = "";

    /// <summary>Output directory from <c>[GenerateTypes(OutputDir = "...")]</c>.</summary>
    public string OutputDir { get; set; } = ".";

    /// <summary>Targets requested via <c>[GenerateTypes(Targets = ...)]</c>.</summary>
    public TypeTarget Targets { get; set; }

    public List<SchemaProperty> Properties { get; } = new();

    /// <summary>Optional per-target name override from <c>[TsName]</c> / <c>[OpenApiSchemaName]</c>.</summary>
    public string? TsNameOverride { get; set; }
    public string? OpenApiNameOverride { get; set; }

    public bool TsIgnore { get; set; }
    public bool OpenApiIgnore { get; set; }

    /// <summary>Populated when the class carries <c>[CrudApi]</c>. <c>null</c> = not a CRUD endpoint.</summary>
    public CrudApiInfo? Crud { get; set; }

    /// <summary>
    /// Shadow of <see cref="GlobalSettings.HasQueryDsl"/>, stamped onto the class
    /// at emit time so emitter helpers can read it without threading settings around.
    /// </summary>
    public bool QueryDsl { get; set; }

    /// <summary>
    /// True when one of this class's properties carries <c>[JsonExtensionData]</c>
    /// (System.Text.Json or Newtonsoft). The property itself is filtered out of
    /// emission and the schema gains <c>additionalProperties</c> (OpenAPI) /
    /// index signature (TypeScript) / <c>extra='allow'</c> (Pydantic).
    /// </summary>
    public bool AllowsAdditionalProperties { get; set; }

    /// <summary>
    /// Mapped value type for <see cref="AllowsAdditionalProperties"/>. <c>null</c>
    /// when the dictionary value type is <c>object</c> / <c>JsonElement</c> — emitters
    /// fall back to a permissive marker (<c>true</c> in OpenAPI, <c>unknown</c> in TS).
    /// </summary>
    public string? AdditionalPropertiesValueCSharpType { get; set; }

    /// <summary>
    /// Fully-qualified C# name of the immediate base type, or <c>null</c> for <c>object</c>
    /// (or <c>System.ValueType</c> for structs). Used by the emitters to decide whether
    /// to wire inheritance — if the base is present in the model as another
    /// <c>[GenerateTypes]</c> class, output becomes <c>allOf</c> / <c>extends</c>; otherwise
    /// the base's properties were pre-inlined by the parser so the output stays flat.
    /// </summary>
    public string? BaseClassFullName { get; set; }
}

internal sealed class SchemaProperty
{
    /// <summary>C# property name as written.</summary>
    public string SourceName { get; set; } = "";

    /// <summary>
    /// Concrete C# type expression (e.g. <c>"int"</c>, <c>"string?"</c>,
    /// <c>"List&lt;Order&gt;"</c>) — used by emitters to resolve target type.
    /// </summary>
    public string CSharpTypeFullName { get; set; } = "";

    /// <summary>True if the property is nullable in the C# source (NRT or <c>T?</c>).</summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// Source <see cref="Location"/> of the property's declaration, used when the
    /// generator needs to surface a diagnostic about this specific property
    /// (e.g. <c>TG0002 Unsupported type → 'unknown'</c>). Null when discovery
    /// added the property from a symbol with no source (metadata-only reference).
    /// </summary>
    public Location? Location { get; set; }

    /// <summary>Per-target name overrides.</summary>
    public string? TsNameOverride { get; set; }
    public string? OpenApiNameOverride { get; set; }

    /// <summary>Per-target type overrides.</summary>
    public string? TsTypeOverride { get; set; }

    /// <summary>
    /// Optional module specifier for the <see cref="TsTypeOverride"/> symbol(s).
    /// When set, the TypeScript emitter adds an <c>import { … } from '&lt;path&gt;';</c>
    /// line at the top of the file, listing every PascalCase identifier appearing in
    /// <see cref="TsTypeOverride"/>. <c>null</c> means the type expression doesn't
    /// reference an external symbol — primitives, literal unions, etc.
    /// </summary>
    public string? TsImportFrom { get; set; }

    /// <summary>
    /// Set when the property carries <c>[TsType&lt;T&gt;]</c> — the FQN of
    /// <c>T</c>. Resolved late (after discovery) by
    /// <see cref="T:ZibStack.NET.TypeGen.Generator.SchemaParser.ResolveGenericTsTypeReferences"/>:
    /// the referenced SchemaClass/SchemaEnum gives the emitted TS name and
    /// <see cref="TsImportFrom"/> falls out of its <c>OutputDir</c>. Null when
    /// the property uses the string-form <c>[TsType("Foo")]</c> or no override.
    /// </summary>
    public string? TsTypeTargetCSharpFqn { get; set; }

    /// <summary>OpenAPI annotations from <c>[OpenApiProperty]</c> and per-property fluent overrides.</summary>
    public string? OpenApiFormat { get; set; }
    public object? OpenApiExample { get; set; }
    public string? OpenApiDescription { get; set; }
    public bool? OpenApiNullableOverride { get; set; }

    /// <summary>Fluent-only — overrides the inferred primary OpenAPI type (e.g. force decimal → string).</summary>
    public string? OpenApiTypeOverride { get; set; }

    /// <summary>Fluent-only — emit as <c>$ref</c> to a named external schema instead of the inferred shape.</summary>
    public string? OpenApiRefOverride { get; set; }

    // ── constraints read from DataAnnotations / ZibStack.Validation attributes ──

    /// <summary>Minimum string length / array item count (<c>[MinLength]</c>, <c>[StringLength(_, MinimumLength=_)]</c>, <c>[ZMinLength]</c>, <c>[ZNotEmpty]</c>).</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximum string length / array item count (<c>[MaxLength]</c>, <c>[StringLength]</c>, <c>[ZMaxLength]</c>).</summary>
    public int? MaxLength { get; set; }

    /// <summary>Inclusive lower bound (<c>[Range]</c>, <c>[ZRange]</c>).</summary>
    public double? Minimum { get; set; }

    /// <summary>Inclusive upper bound (<c>[Range]</c>, <c>[ZRange]</c>).</summary>
    public double? Maximum { get; set; }

    /// <summary>Regex pattern (<c>[RegularExpression]</c>, <c>[ZMatch]</c>).</summary>
    public string? Pattern { get; set; }

    public bool TsIgnore { get; set; }
    public bool OpenApiIgnore { get; set; }
}

internal sealed class SchemaEnum
{
    public string CSharpFullName { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string EmittedName { get; set; } = "";
    public string OutputDir { get; set; } = ".";
    public TypeTarget Targets { get; set; }
    public List<SchemaEnumMember> Members { get; } = new();

    public string? TsNameOverride { get; set; }
    public string? OpenApiNameOverride { get; set; }
    public bool TsIgnore { get; set; }
    public bool OpenApiIgnore { get; set; }

    /// <summary>
    /// <c>true</c> when the enum carries
    /// <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> (or the generic
    /// <c>JsonStringEnumConverter&lt;T&gt;</c>, or Newtonsoft's
    /// <c>StringEnumConverter</c>) — meaning runtime JSON is the member name,
    /// not the underlying integer. Emitters use this to choose string-valued
    /// TypeScript enum members and <c>(str, Enum)</c> Python bases, matching
    /// what the consumer will actually see on the wire.
    /// </summary>
    public bool IsStringSerialized { get; set; }
}

internal sealed class SchemaEnumMember
{
    public string Name { get; set; } = "";
    public long Value { get; set; }
}

/// <summary>
/// Resolved global settings — combination of <c>typegen.config.json</c> AdditionalFile
/// (if present) and the project's <see cref="ITypeGenConfigurator"/> implementation
/// (if present). Per-class / per-property attributes override individual fields.
/// </summary>
internal sealed class GlobalSettings
{
    public TypeScriptSettings TypeScript { get; set; } = new();
    public OpenApiSettings OpenApi { get; set; } = new();
    public PythonSettings Python { get; set; } = new();

    /// <summary>
    /// Set by the generator when <c>ZibStack.NET.Query</c> is referenced by the
    /// compilation. The Dto CRUD list endpoints bind <c>filter</c>/<c>sort</c>/
    /// <c>select</c>/<c>count</c> query-string params only when that package is
    /// available — so the OpenAPI spec matches the real endpoint shape.
    /// </summary>
    public bool HasQueryDsl { get; set; }
}
