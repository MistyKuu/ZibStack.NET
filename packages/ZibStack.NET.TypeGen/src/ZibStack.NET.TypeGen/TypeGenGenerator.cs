using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ZibStack.NET.TypeGen.Generator;

/// <summary>
/// Roslyn incremental generator entry point. Walks the compilation for
/// <c>[GenerateTypes]</c>-annotated classes/enums, builds a
/// <see cref="SchemaModel"/>, runs the requested emitters, and writes the result
/// out as a single shim <c>.g.cs</c> manifest in <c>obj/generated</c>. A
/// companion MSBuild task in this package's <c>build/.targets</c> file reads the
/// manifest post-build and copies the generated <c>.ts</c> / <c>.yaml</c> files
/// into the user's configured output directories.
///
/// <para>
/// The shim-and-task split is necessary because Roslyn source generators can
/// only emit <c>.cs</c> files into the compilation — they cannot write to
/// arbitrary paths on disk during code generation.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TypeGenGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Pull every type declaration that carries [GenerateTypes] OR [CrudApi].
        // [CrudApi]-only types don't drive emission but are tracked so we can warn
        // (TG0014) that they're invisible without [GenerateTypes].
        var classes = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax or EnumDeclarationSyntax,
            transform: static (ctx, _) =>
            {
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol;
                if (symbol is null) return null;
                var hasGen = SchemaParser.HasGenerateTypes(symbol);
                var hasCrud = SchemaParser.HasCrudApi(symbol);
                if (!hasGen && !hasCrud) return null;
                return symbol;
            })
            .Where(static s => s is not null);

        var compilation = context.CompilationProvider;

        var combined = classes.Collect().Combine(compilation);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var (symbols, compilation) = source;
            if (symbols.IsDefaultOrEmpty) return;

            // Warn about [CrudApi]-only classes that TypeGen can't emit paths for —
            // they need [GenerateTypes] so we discover them via the same provider.
            // (Actually they're already in `symbols` thanks to the dual predicate above,
            //  but the OpenApiEmitter would skip them; surface the cause explicitly.)
            foreach (var sym in symbols)
            {
                if (sym is null) continue;
                if (!SchemaParser.HasGenerateTypes(sym) && SchemaParser.HasCrudApi(sym))
                    spc.ReportDiagnostic(Diagnostic.Create(
                        TypeGenDiagnostics.CrudApiWithoutGenerateTypes,
                        sym.Locations.FirstOrDefault() ?? Location.None,
                        sym.ToDisplayString()));
            }

            // Parse ITypeGenConfigurator DSL first — global settings feed the emitters,
            // per-type overrides merge into SchemaClass/SchemaEnum below.
            var config = ConfiguratorParser.Parse(compilation, spc.ReportDiagnostic);

            var model = new SchemaModel();
            foreach (var sym in symbols)
            {
                if (sym is null) continue;

                // Generic types are out of MVP scope — surface a clear diagnostic
                // instead of emitting broken output.
                if (sym.IsGenericType)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        TypeGenDiagnostics.GenericType,
                        sym.Locations.FirstOrDefault() ?? Location.None,
                        sym.ToDisplayString()));
                    continue;
                }

                if (sym.TypeKind == TypeKind.Enum)
                {
                    var en = SchemaParser.ParseEnum(sym);
                    if (en is null) continue;
                    ApplyFluentToEnum(en, config);
                    ReportTargetsIfEmpty(spc, sym, en.Targets);
                    ReportInvalidOutputDirIfEmpty(spc, sym, en.OutputDir);
                    model.Enums.Add(en);
                }
                else
                {
                    var cls = SchemaParser.ParseClass(sym);
                    if (cls is null) continue;
                    ApplyFluentToClass(cls, config);
                    ReportTargetsIfEmpty(spc, sym, cls.Targets);
                    ReportInvalidOutputDirIfEmpty(spc, sym, cls.OutputDir);
                    model.Classes.Add(cls);

                    // Synthesize Create/Update/Response companion schemas from Dto's attributes.
                    // Roslyn doesn't let TypeGen see Dto's generated records in the same pass,
                    // so we use shared/DtoSemantics.cs (single source of truth with Dto) to
                    // recreate the schemas ourselves. Output targets the SAME emitters the
                    // parent does — TypeScript companions when parent is TS, OpenAPI when
                    // parent is OpenAPI, etc. $refs / cross-file imports resolve cleanly.
                    if ((cls.Targets & (TypeTarget.OpenApi | TypeTarget.TypeScript | TypeTarget.Python)) != 0
                        && !cls.OpenApiIgnore)
                    {
                        SynthesizeAuxiliaryForVariant(model, cls, sym, Shared.DtoTarget.Create);
                        SynthesizeAuxiliaryForVariant(model, cls, sym, Shared.DtoTarget.Update);
                        SynthesizeAuxiliaryForVariant(model, cls, sym, Shared.DtoTarget.Response);
                    }
                }
            }


            // Fluent-only discovery: any b.ForType<T>().WithGeneratedTypes(...) registers T
            // for emission even if it has no [GenerateTypes] attribute. Lets users keep
            // model files free of generation markers — central config in TypeGenConfig.cs.
            // Two-pass: discovery + companion synthesis FIRST so synthesized aux schemas
            // exist; THEN apply foreign-name overrides like b.ForType<CreateArticleRequest>()
            // .TsName(...) that target those synthesized schemas by simple name.
            if (config is not null)
            {
                // First pass — opt-in discovery + companion synthesis.
                foreach (var kvp in config.PerType)
                {
                    if (kvp.Value.FluentTargets is not int fluentTargets) continue;
                    // Skip if the type was already discovered via attribute.
                    if (model.Classes.Any(c => c.CSharpFullName == kvp.Key)) continue;

                    var sym = compilation.GetTypeByMetadataName(kvp.Key);
                    if (sym is null)
                    {
                        // Fallback path for Dto-generated companion types (Create{X}Request /
                        // Update{X}Request / {X}Response). Roslyn won't resolve them as
                        // symbols (cross-generator visibility), but the synthesis pass that
                        // ran earlier may have already added a SchemaClass with this simple
                        // name. Apply this entry's overrides directly — ApplyFluentToClass
                        // can't be used because it looks up by CSharpFullName, while the
                        // fluent key here is the bare syntactic name without namespace.
                        var existingAux = model.Classes.FirstOrDefault(c =>
                            c.SourceName == kvp.Key || c.EmittedName == kvp.Key);
                        if (existingAux is not null)
                        {
                            existingAux.Targets |= (TypeTarget)fluentTargets;
                            existingAux.TsNameOverride ??= kvp.Value.TsName;
                            existingAux.OpenApiNameOverride ??= kvp.Value.OpenApiName;
                            if (kvp.Value.Ignore) { existingAux.TsIgnore = true; existingAux.OpenApiIgnore = true; }
                            existingAux.TsIgnore |= kvp.Value.TsIgnore;
                            existingAux.OpenApiIgnore |= kvp.Value.OpenApiIgnore;
                        }
                        continue;
                    }

                    var dir = !string.IsNullOrEmpty(kvp.Value.OutputDir) ? kvp.Value.OutputDir!
                            : !string.IsNullOrEmpty(config.Settings.TypeScript.OutputDir) ? config.Settings.TypeScript.OutputDir!
                            : ".";
                    var aux = SchemaParser.ParseAuxiliaryClass(sym, (TypeTarget)fluentTargets, dir);
                    if (aux is null) continue;
                    ApplyFluentToClass(aux, config);
                    model.Classes.Add(aux);

                    // Same companion-synthesis the attribute path runs — fluent-discovered
                    // classes get Create/Update/Response companions in the same target set.
                    // Force=true bypasses the [CreateDto]/[CrudApi] attribute gate; for
                    // fluent-discovered types we trust the user's intent (Dto-side fluent
                    // may be driving DTO generation without attributes either).
                    if ((aux.Targets & (TypeTarget.OpenApi | TypeTarget.TypeScript | TypeTarget.Python)) != 0
                        && !aux.OpenApiIgnore)
                    {
                        SynthesizeAuxiliaryForVariant(model, aux, sym, Shared.DtoTarget.Create, force: true);
                        SynthesizeAuxiliaryForVariant(model, aux, sym, Shared.DtoTarget.Update, force: true);
                        SynthesizeAuxiliaryForVariant(model, aux, sym, Shared.DtoTarget.Response, force: true);
                    }
                }

                // Second pass — overrides without WithGeneratedTypes. Targets synthesized
                // aux schemas added by the discovery pass (and by the attribute pipeline).
                // Match by simple name since Dto-generated companions can't be resolved as
                // symbols (fluent stores syntactic name as fallback for those).
                foreach (var kvp in config.PerType)
                {
                    if (kvp.Value.FluentTargets is not null) continue;
                    var existingAux = model.Classes.FirstOrDefault(c =>
                        c.SourceName == kvp.Key || c.EmittedName == kvp.Key);
                    if (existingAux is null) continue;
                    existingAux.TsNameOverride ??= kvp.Value.TsName;
                    existingAux.OpenApiNameOverride ??= kvp.Value.OpenApiName;
                    if (kvp.Value.Ignore) { existingAux.TsIgnore = true; existingAux.OpenApiIgnore = true; }
                    existingAux.TsIgnore |= kvp.Value.TsIgnore;
                    existingAux.OpenApiIgnore |= kvp.Value.OpenApiIgnore;
                }
            }

            if (model.Classes.Count == 0 && model.Enums.Count == 0) return;

            var settings = config?.Settings ?? new GlobalSettings();
            // Detect ZibStack.NET.Query presence by probing a well-known type. When
            // referenced, the Dto CRUD list endpoint binds additional query-string params
            // (filter/sort/select/count) — the OpenAPI paths must match that shape.
            settings.HasQueryDsl = compilation.GetTypeByMetadataName("ZibStack.NET.Query.FilterParser") is not null;

            // Run each requested emitter.
            var allFiles = new List<EmittedFile>();
            if (RequestsTarget(model, TypeTarget.TypeScript))
                allFiles.AddRange(TypeScriptEmitter.Emit(model, settings));
            if (RequestsTarget(model, TypeTarget.OpenApi))
                allFiles.AddRange(OpenApiEmitter.Emit(model, settings));
            if (RequestsTarget(model, TypeTarget.Python))
                allFiles.AddRange(PythonEmitter.Emit(model, settings));

            if (allFiles.Count == 0) return;

            // Emit the manifest as a .g.cs containing string constants. The companion
            // MSBuild task reads this file at build time, decodes the constants, and
            // writes the actual .ts/.yaml files to the user's OutputDir.
            var manifest = BuildManifest(allFiles);
            spc.AddSource("ZibStack.TypeGen.Manifest.g.cs", SourceText.From(manifest, Encoding.UTF8));
        });
    }

    /// <summary>
    /// Fills in attribute-less fields from the configurator's per-type overrides.
    /// Attributes always win — fluent only fills the gap. Ignore flags are OR-merged
    /// so the fluent can widen, never narrow.
    /// </summary>
    private static void ApplyFluentToClass(SchemaClass cls, ConfiguratorParser.Parsed? config)
    {
        var o = LookupOverride(cls.CSharpFullName, config);
        if (o is null) return;
        cls.TsNameOverride ??= o.TsName;
        cls.OpenApiNameOverride ??= o.OpenApiName;
        // Treat "." (the default when no attribute OutputDir) as unset for merge purposes.
        if (cls.OutputDir == "." && !string.IsNullOrEmpty(o.OutputDir)) cls.OutputDir = o.OutputDir!;
        if (o.Ignore) { cls.TsIgnore = true; cls.OpenApiIgnore = true; }
        cls.TsIgnore |= o.TsIgnore;
        cls.OpenApiIgnore |= o.OpenApiIgnore;

        // Per-property fluent overrides — attribute values already on SchemaProperty win,
        // fluent only fills nulls. Boolean ignore flags OR-merge so fluent can widen.
        foreach (var prop in cls.Properties)
        {
            if (!o.Properties.TryGetValue(prop.SourceName, out var po)) continue;
            prop.TsNameOverride ??= po.TsName;
            prop.TsTypeOverride ??= po.TsType;
            prop.OpenApiNameOverride ??= po.OpenApiName;
            prop.OpenApiTypeOverride ??= po.OpenApiType;
            prop.OpenApiRefOverride ??= po.OpenApiRef;
            prop.OpenApiFormat ??= po.OpenApiFormat;
            prop.OpenApiDescription ??= po.OpenApiDescription;
            prop.OpenApiNullableOverride ??= po.OpenApiNullable;
            if (po.Ignore) { prop.TsIgnore = true; prop.OpenApiIgnore = true; }
            prop.TsIgnore |= po.TsIgnore;
            prop.OpenApiIgnore |= po.OpenApiIgnore;
        }
    }

    private static void ApplyFluentToEnum(SchemaEnum en, ConfiguratorParser.Parsed? config)
    {
        var o = LookupOverride(en.CSharpFullName, config);
        if (o is null) return;
        en.TsNameOverride ??= o.TsName;
        en.OpenApiNameOverride ??= o.OpenApiName;
        if (en.OutputDir == "." && !string.IsNullOrEmpty(o.OutputDir)) en.OutputDir = o.OutputDir!;
        if (o.Ignore) { en.TsIgnore = true; en.OpenApiIgnore = true; }
        en.TsIgnore |= o.TsIgnore;
        en.OpenApiIgnore |= o.OpenApiIgnore;
    }

    /// <summary>
    /// Emits a synthetic <c>Create{X}Request</c> / <c>Update{X}Request</c> / <c>{X}Response</c>
    /// schema for <paramref name="parent"/> by walking its properties and applying the
    /// shared <see cref="Shared.DtoSemantics"/> filter for <paramref name="variant"/>.
    /// Only fires when the parent carries a Dto attribute that would make the Dto
    /// generator produce that variant in real code (checked via <c>HasDtoAttributeFor</c>).
    /// </summary>
    private static void SynthesizeAuxiliaryForVariant(SchemaModel model, SchemaClass parent, INamedTypeSymbol parentSymbol, Shared.DtoTarget variant, bool force = false)
    {
        // Attribute path requires a Dto attribute to be present (CreateDto / CrudApi /
        // etc.) — that's the signal "Dto will generate this companion, we should sync".
        // Fluent path passes force=true: caller has already opted in by listing the
        // type in IDtoConfigurator, so we trust the intent regardless of attributes.
        if (!force && !Shared.DtoSemantics.HasDtoAttributeFor(parentSymbol, variant)) return;

        var schemaName = Shared.DtoSemantics.GetDefaultDtoName(parent.SourceName, variant);
        // Dedupe: if the user handwrote their own [GenerateTypes] partial, renamed a
        // class to that name via OpenApiName override, or a prior synthesis already
        // ran — don't emit a duplicate schema key.
        if (model.Classes.Any(c =>
                c.EmittedName == schemaName ||
                c.SourceName == schemaName ||
                c.OpenApiNameOverride == schemaName))
            return;

        var aux = new SchemaClass
        {
            CSharpFullName = $"{GetNamespace(parent.CSharpFullName)}.{schemaName}",
            SourceName = schemaName,
            EmittedName = schemaName,
            // Inherit parent's targets so the companion gets emitted by every emitter
            // the parent does (TS / OpenAPI / Python). Was hard-coded OpenApi only.
            Targets = parent.Targets,
            OutputDir = parent.OutputDir,
        };

        // Response excludes the response-key ignores; Create/Update typically exclude the
        // primary key (Id). Walk the parent symbol's properties directly — SchemaClass.Properties
        // already captured attribute overrides but we need the RAW symbol for per-variant filtering.
        foreach (var propSymbol in parentSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (propSymbol.IsStatic || propSymbol.IsIndexer) continue;
            if (propSymbol.DeclaredAccessibility != Accessibility.Public) continue;
            if (!Shared.DtoSemantics.IsIncluded(propSymbol, variant)) continue;

            // Re-use the matching SchemaProperty built for the parent so validation
            // constraints (minLength, etc.) and overrides flow through unchanged.
            var original = parent.Properties.FirstOrDefault(p => p.SourceName == propSymbol.Name);
            if (original is null) continue;
            aux.Properties.Add(original);
        }

        // Empty aux (all properties filtered out) — don't bother adding.
        if (aux.Properties.Count == 0) return;
        model.Classes.Add(aux);
    }

    private static string GetNamespace(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName.Substring(0, dot) : "";
    }

    private static ConfiguratorParser.PerTypeOverrides? LookupOverride(string fullName, ConfiguratorParser.Parsed? config)
    {
        if (config is null) return null;
        return config.PerType.TryGetValue(fullName, out var o) ? o : null;
    }

    private static bool RequestsTarget(SchemaModel model, TypeTarget target)
    {
        foreach (var c in model.Classes) if ((c.Targets & target) != 0) return true;
        foreach (var e in model.Enums) if ((e.Targets & target) != 0) return true;
        return false;
    }

    private static void ReportTargetsIfEmpty(SourceProductionContext spc, ISymbol sym, TypeTarget targets)
    {
        if (targets == TypeTarget.None)
            spc.ReportDiagnostic(Diagnostic.Create(
                TypeGenDiagnostics.NoTargets,
                sym.Locations.FirstOrDefault() ?? Location.None,
                sym.Name));
    }

    private static void ReportInvalidOutputDirIfEmpty(SourceProductionContext spc, ISymbol sym, string outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir))
            spc.ReportDiagnostic(Diagnostic.Create(
                TypeGenDiagnostics.InvalidOutputDir,
                sym.Locations.FirstOrDefault() ?? Location.None,
                outputDir ?? "<null>",
                sym.Name));
    }

    /// <summary>
    /// Serializes the emitted file list into a deterministic .g.cs source the
    /// companion MSBuild task can parse. Format: each file becomes one
    /// <c>internal const string FileN = "..."</c> with sibling
    /// <c>OutputDirN</c> / <c>FileNameN</c> / <c>TargetN</c> constants. The class
    /// stays internal and editor-hidden so it doesn't pollute IntelliSense.
    /// </summary>
    private static string BuildManifest(IReadOnlyList<EmittedFile> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by ZibStack.NET.TypeGen — do not edit/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace ZibStack.NET.TypeGen.Generated;");
        sb.AppendLine();
        sb.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        sb.AppendLine("internal static class TypeGenManifest");
        sb.AppendLine("{");
        sb.AppendLine($"    internal const int FileCount = {files.Count};");
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            sb.AppendLine($"    internal const string Target{i} = {Verbatim(f.Target.ToString())};");
            sb.AppendLine($"    internal const string OutputDir{i} = {Verbatim(f.OutputDir)};");
            sb.AppendLine($"    internal const string FileName{i} = {Verbatim(f.FileName)};");
            sb.AppendLine($"    internal const string Content{i} = {Verbatim(f.Content)};");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Verbatim(string s) => "@\"" + s.Replace("\"", "\"\"") + "\"";
}
