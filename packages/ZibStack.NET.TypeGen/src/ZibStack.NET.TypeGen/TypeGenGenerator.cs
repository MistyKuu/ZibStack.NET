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

        // ProjectDir comes via MSBuild — needed so generator-side direct writes can
        // resolve relative OutputDir paths the same way the MSBuild task does. Falls
        // back to "" if MSBuildProjectDirectory isn't in the analyzer config (e.g.
        // running under a non-MSBuild host).
        var projectDir = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            provider.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var dir)
                ? dir : "");

        var combined = classes.Collect().Combine(compilation).Combine(projectDir);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var ((symbols, compilation), projectDir) = source;
            // Note: do NOT early-return on empty symbols — the configurator's
            // .WithGeneratedTypes(...) fluent discovery (handled below) is a valid
            // sole driver of generation. The bottom of this callback exits anyway
            // if nothing accumulates in the model.
            if (symbols.IsDefault) symbols = ImmutableArray<INamedTypeSymbol?>.Empty;

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
                        // Symbol unresolvable — typically a Dto-generated companion the
                        // user is opting into explicitly via fluent. If the name matches
                        // the Dto naming pattern, find parent in the fluent set (its
                        // Symbol is stored even when the parent has no .WithGeneratedTypes
                        // — the user wrote b.ForType<X>() which resolved at parse time)
                        // and synthesize that single variant.
                        if (TryParseCompanionName(kvp.Key, out var parentName, out var variant))
                        {
                            // Try parent from fluent set first; otherwise scan the user's
                            // assembly. The assembly scan means users can write just
                            // b.ForType<CreateArticleRequest>().WithGeneratedTypes(TS) —
                            // no need for a separate b.ForType<Article>() anchor line.
                            var parentEntry = config.PerType.FirstOrDefault(e =>
                                e.Value.Symbol is not null && e.Value.Symbol.Name == parentName);
                            var parentSym = parentEntry.Value?.Symbol
                                ?? FindTypeInAssembly(compilation.Assembly.GlobalNamespace, parentName);
                            if (parentSym is not null)
                            {
                                // Synthesize from parent symbol — needs a transient SchemaClass
                                // wrapping the parent for ParseAuxiliaryClass-based property walk.
                                var parentCls = SchemaParser.ParseAuxiliaryClass(parentSym, (TypeTarget)fluentTargets, ".");
                                if (parentCls is not null)
                                {
                                    SynthesizeAuxiliaryForVariant(model, parentCls, parentSym, variant, force: true);
                                    var synthAux = model.Classes.FirstOrDefault(c => c.SourceName == kvp.Key);
                                    if (synthAux is not null)
                                    {
                                        synthAux.Targets = (TypeTarget)fluentTargets;
                                        synthAux.TsNameOverride ??= kvp.Value.TsName;
                                        synthAux.OpenApiNameOverride ??= kvp.Value.OpenApiName;
                                        if (!string.IsNullOrEmpty(kvp.Value.OutputDir))
                                            synthAux.OutputDir = kvp.Value.OutputDir!;
                                        else if (!string.IsNullOrEmpty(config.Settings.TypeScript.OutputDir))
                                            synthAux.OutputDir = config.Settings.TypeScript.OutputDir!;
                                    }
                                }
                            }
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

                    // Companion synthesis only when the parent has the gate attributes
                    // ([CreateDto] / [CrudApi] / etc.). For pure-fluent Article without
                    // any Dto attributes, the gate fails and we emit ONLY the parent.
                    // To opt into a specific companion, use:
                    //   b.ForType<CreateArticleRequest>().WithGeneratedTypes(TS)
                    // — that goes through the second pass below which detects the naming
                    // pattern and synthesizes that one variant from the parent's properties.
                    if ((aux.Targets & (TypeTarget.OpenApi | TypeTarget.TypeScript | TypeTarget.Python)) != 0
                        && !aux.OpenApiIgnore)
                    {
                        SynthesizeAuxiliaryForVariant(model, aux, sym, Shared.DtoTarget.Create);
                        SynthesizeAuxiliaryForVariant(model, aux, sym, Shared.DtoTarget.Update);
                        SynthesizeAuxiliaryForVariant(model, aux, sym, Shared.DtoTarget.Response);
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

            // Iterate seed → base discovery → nested discovery until the model
            // stops growing. Each pass can unlock new work for the others:
            //   * SeedGeneric pulls in a [TsType<A>] target; that target's base
            //     chain needs DiscoverBaseClasses,
            //   * DiscoverBaseClasses pulls in a base; that base's own properties
            //     may hit [TsType<T>] overrides or nested user types — so
            //     SeedGeneric + DiscoverTransitive get another look.
            // Fluent overrides get applied inside the loop too — any freshly
            // added class goes through ApplyFluentToClass so per-property
            // TsTypeTargetCSharpFqn from the configurator is visible to the next
            // SeedGeneric iteration. Terminates when no pass adds anything.
            // Each pass is strict-additive, so a hard iteration cap is a safety
            // net — should exit in 2-3 turns for realistic graphs. If we hit the
            // cap something's pathological (cyclic symbol equality maybe?) —
            // better to emit a possibly-incomplete model than spin forever.
            int lastCount;
            int guard = 0;
            const int maxIter = 16;
            do
            {
                lastCount = model.Classes.Count + model.Enums.Count;
                var clsBefore = model.Classes.Count;
                var enumBefore = model.Enums.Count;
                SchemaParser.SeedGenericTsTypeTargets(model, compilation);
                SchemaParser.DiscoverBaseClasses(model, compilation);
                SchemaParser.DiscoverTransitive(model, compilation);
                for (int i = clsBefore; i < model.Classes.Count; i++)
                    ApplyFluentToClass(model.Classes[i], config);
                for (int i = enumBefore; i < model.Enums.Count; i++)
                    ApplyFluentToEnum(model.Enums[i], config);
            } while (model.Classes.Count + model.Enums.Count > lastCount && ++guard < maxIter);

            // Late-bind `[TsType<T>]` references: replace each property's fallback
            // TsTypeOverride with T's emitted TS name, and when the user didn't
            // supply an ImportFrom, compute the relative import path from the owning
            // class's OutputDir to the target's. Must run AFTER discovery + fluent
            // so target names / OutputDirs are final.
            SchemaParser.ResolveGenericTsTypeReferences(model);

            if (model.Classes.Count == 0 && model.Enums.Count == 0) return;

            // Surface every property whose C# type won't render into a sane TS /
            // OpenAPI expression (fell back to `unknown` / `object` because the type
            // isn't primitive, isn't in the model, isn't overridden, isn't ignored).
            // This used to fail silently and the user was left with unusable output
            // until they spotted the `unknown` in their .ts files by chance.
            ReportUntranslatableProperties(spc, model);

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

            // Live regeneration: also write files directly during code generation so
            // they refresh on save in the IDE (not just at `dotnet build` time). This
            // technically violates the "generators are pure" guideline — wrap in
            // try/catch since some IDE / language-server contexts sandbox analyzer I/O.
            // Failure surfaces as TG0020 (Info severity) and falls back to the MSBuild-
            // task path at build time so output isn't lost.
            if (!string.IsNullOrEmpty(projectDir))
                TryWriteFilesDirectly(allFiles, projectDir, spc);
        });
    }

    /// <summary>
    /// Writes emitted files directly to disk from inside the generator. Same shape as
    /// the MSBuild task in build/ZibStack.NET.TypeGen.targets (content-equality skip,
    /// banner-based stale sweep) so both paths agree on what's on disk. On I/O failure
    /// (sandboxed analyzer host like VS Code C# Dev Kit / Rider) reports TG0020 and
    /// returns — the MSBuild task path will write the same files at build time.
    /// </summary>
#pragma warning disable RS1035 // File I/O is intentional here — see method summary.
    private static void TryWriteFilesDirectly(IReadOnlyList<EmittedFile> files, string projectDir, SourceProductionContext spc)
    {
        var written = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var touchedDirs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // Pre-seed touchedDirs with every OutputDir the current emit set targets.
        // Sweep has to visit these even if individual writes fail — otherwise a
        // rename (A.ts → B.ts, where A.ts exists but B.ts's write errors out) would
        // leave the stale A.ts in place. Collecting directories is cheap and never
        // trips on sandboxed-I/O the way actual writes do.
        foreach (var f in files)
        {
            var fullDir = System.IO.Path.IsPathRooted(f.OutputDir)
                ? f.OutputDir
                : System.IO.Path.Combine(projectDir, f.OutputDir);
            try { touchedDirs.Add(System.IO.Path.GetFullPath(fullDir)); } catch { /* bad path, skip */ }
        }

        bool anyWriteFailed = false;
        foreach (var f in files)
        {
            string fullPath;
            try
            {
                var fullDir = System.IO.Path.IsPathRooted(f.OutputDir)
                    ? f.OutputDir
                    : System.IO.Path.Combine(projectDir, f.OutputDir);
                System.IO.Directory.CreateDirectory(fullDir);
                fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullDir, f.FileName));
                written.Add(fullPath);

                // Skip if unchanged — keeps mtime stable, prevents file-watcher spam
                // downstream (Vite, webpack, etc.) on every keystroke that triggers
                // a regen but produces identical output.
                if (System.IO.File.Exists(fullPath) && System.IO.File.ReadAllText(fullPath) == f.Content)
                    continue;
                System.IO.File.WriteAllText(fullPath, f.Content);
            }
            catch (System.Exception ex)
            {
                // Surface once per run — sandboxed hosts hit this for every file.
                // Still report once-per-file because IDE Problems list dedupes by
                // location. Keep going so the sweep at the end still gets to run
                // (renames should drop stale files even if some writes bombed).
                if (!anyWriteFailed)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        TypeGenDiagnostics.LiveRegenSandboxed,
                        Location.None,
                        f.FileName, ex.GetType().Name + ": " + ex.Message));
                }
                anyWriteFailed = true;
            }
        }

        // Sweep stale: only files carrying our @generated banner that aren't in
        // the current run. Same heuristic as the MSBuild task.
        try
        {
            foreach (var dir in touchedDirs)
            {
                if (!System.IO.Directory.Exists(dir)) continue;
                foreach (var existing in System.IO.Directory.EnumerateFiles(dir))
                {
                    var fullExisting = System.IO.Path.GetFullPath(existing);
                    if (written.Contains(fullExisting)) continue;
                    try
                    {
                        using var fs = new System.IO.FileStream(fullExisting,
                            System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
                        var buf = new byte[200];
                        int n = fs.Read(buf, 0, buf.Length);
                        var head = System.Text.Encoding.UTF8.GetString(buf, 0, n);
                        if (head.IndexOf("@generated by ZibStack.NET.TypeGen", System.StringComparison.Ordinal) < 0) continue;
                    }
                    catch { continue; }

                    try { System.IO.File.Delete(fullExisting); } catch { /* locked / read-only — leave it */ }
                }
            }
        }
        catch
        {
            // Sweep is best-effort — the MSBuild task does the same sweep at build time.
        }
    }
#pragma warning restore RS1035

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
            prop.TsImportFrom ??= po.TsImportFrom;
            prop.TsTypeTargetCSharpFqn ??= po.TsTypeTargetCSharpFqn;
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
    /// Walks the user's assembly looking for a type with matching simple name.
    /// Used by the fluent companion-synthesis fallback when the parent type isn't
    /// listed in the fluent config — lets users write a single
    /// <c>b.ForType&lt;CreateArticleRequest&gt;().WithGeneratedTypes(TS)</c> entry
    /// without needing a separate <c>b.ForType&lt;Article&gt;()</c> anchor line.
    /// </summary>
    private static INamedTypeSymbol? FindTypeInAssembly(INamespaceSymbol ns, string simpleName)
    {
        foreach (var t in ns.GetTypeMembers())
            if (t.Name == simpleName) return t;
        foreach (var nested in ns.GetNamespaceMembers())
        {
            var found = FindTypeInAssembly(nested, simpleName);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// Parses Dto's companion DTO naming convention. Returns true (with parent name and
    /// variant) when <paramref name="name"/> matches <c>Create{X}Request</c>,
    /// <c>Update{X}Request</c>, or <c>{X}Response</c>. Lets the fluent layer opt into a
    /// single companion (e.g. <c>b.ForType&lt;CreateArticleRequest&gt;()</c>) without
    /// emitting siblings.
    /// </summary>
    private static bool TryParseCompanionName(string name, out string parentName, out Shared.DtoTarget variant)
    {
        if (name.StartsWith("Create", System.StringComparison.Ordinal) && name.EndsWith("Request", System.StringComparison.Ordinal))
        {
            parentName = name.Substring(6, name.Length - 6 - "Request".Length);
            variant = Shared.DtoTarget.Create;
            return parentName.Length > 0;
        }
        if (name.StartsWith("Update", System.StringComparison.Ordinal) && name.EndsWith("Request", System.StringComparison.Ordinal))
        {
            parentName = name.Substring(6, name.Length - 6 - "Request".Length);
            variant = Shared.DtoTarget.Update;
            return parentName.Length > 0;
        }
        if (name.EndsWith("Response", System.StringComparison.Ordinal))
        {
            parentName = name.Substring(0, name.Length - "Response".Length);
            variant = Shared.DtoTarget.Response;
            return parentName.Length > 0;
        }
        parentName = ""; variant = Shared.DtoTarget.None;
        return false;
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

        // [JsonExtensionData] propagates to companions — if the parent has a
        // catch-all property, the Create/Update/Response shapes inherit it
        // (the JSON wire still accepts unmapped keys for these DTOs).
        aux.AllowsAdditionalProperties = parent.AllowsAdditionalProperties;
        aux.AdditionalPropertiesValueCSharpType = parent.AdditionalPropertiesValueCSharpType;

        // Empty aux (all properties filtered out) — don't bother adding,
        // unless the schema is purely additionalProperties (still useful as
        // an open-shape contract).
        if (aux.Properties.Count == 0 && !aux.AllowsAdditionalProperties) return;
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
    /// Flags every property the emitters would render as <c>unknown</c> (TS) /
    /// <c>object</c> (OpenAPI) because its C# type is neither a primitive, nor in
    /// the emitted model, nor overridden via <c>[TsType]</c>/<c>[OpenApiProperty]</c>,
    /// nor suppressed via <c>[TsIgnore]</c>/<c>[OpenApiIgnore]</c>. Surfaces as
    /// <c>TG0002</c> with the property's source location so the user sees the
    /// warning in their Errors panel instead of discovering the degraded output
    /// by chance.
    /// </summary>
    private static void ReportUntranslatableProperties(SourceProductionContext spc, SchemaModel model) =>
        ValidateTranslatableProperties(model, spc.ReportDiagnostic);

    /// <summary>
    /// Pure-function variant of <see cref="ReportUntranslatableProperties"/> —
    /// takes a callback so tests can drive it without a <see cref="SourceProductionContext"/>
    /// (which has no public constructor).
    /// </summary>
    internal static void ValidateTranslatableProperties(SchemaModel model, System.Action<Diagnostic> report)
    {
        // Build a fast lookup for every type the emitters know how to render: the
        // in-model classes / enums by FQN plus the primitive C# types the emitters
        // map natively (mirrors TypeScriptEmitter.MapCSharpToTs + OpenApiEmitter).
        var known = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
            "float", "double", "decimal", "string", "char",
            "System.Boolean", "System.Byte", "System.SByte", "System.Int16", "System.UInt16",
            "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
            "System.Single", "System.Double", "System.Decimal", "System.String", "System.Char",
            "System.Guid", "System.DateTime", "System.DateTimeOffset", "System.DateOnly",
            "System.TimeOnly", "System.TimeSpan", "System.Uri", "System.Version",
            "object", "System.Object",
            // Binary / stream types map to string+format in OpenAPI, `string` in TS.
            "byte[]", "System.IO.Stream",
        };
        foreach (var c in model.Classes) known.Add(c.CSharpFullName);
        foreach (var e in model.Enums) known.Add(e.CSharpFullName);

        foreach (var cls in model.Classes)
        {
            if (cls.TsIgnore && cls.OpenApiIgnore) continue;
            var needsTs = (cls.Targets & TypeTarget.TypeScript) != 0 && !cls.TsIgnore;
            var needsOa = (cls.Targets & TypeTarget.OpenApi) != 0 && !cls.OpenApiIgnore;
            if (!needsTs && !needsOa) continue;

            foreach (var prop in cls.Properties)
            {
                // Per-target opt-outs — user already said "don't emit this."
                var checkTs = needsTs && !prop.TsIgnore && prop.TsTypeOverride is null;
                var checkOa = needsOa && !prop.OpenApiIgnore && prop.OpenApiTypeOverride is null && prop.OpenApiRefOverride is null;
                if (!checkTs && !checkOa) continue;

                if (IsTranslatableType(prop.CSharpTypeFullName, known)) continue;

                if (checkTs)
                    report(Diagnostic.Create(
                        TypeGenDiagnostics.UnsupportedType,
                        prop.Location ?? Location.None,
                        cls.SourceName, prop.SourceName, prop.CSharpTypeFullName, "TypeScript"));
                if (checkOa)
                    report(Diagnostic.Create(
                        TypeGenDiagnostics.UnsupportedType,
                        prop.Location ?? Location.None,
                        cls.SourceName, prop.SourceName, prop.CSharpTypeFullName, "OpenAPI"));
            }
        }
    }

    /// <summary>
    /// Checks whether <paramref name="cSharpType"/> unwraps to something in
    /// <paramref name="known"/>. Handles nullable, array, common collection
    /// wrappers (<c>List</c>, <c>Dictionary</c>, <c>IEnumerable</c>, etc.) and
    /// <c>PatchField</c> so the validator doesn't false-positive on legitimate
    /// composite types.
    /// </summary>
    private static bool IsTranslatableType(string cSharpType, HashSet<string> known)
    {
        var t = cSharpType.TrimEnd('?').Trim();
        if (known.Contains(t)) return true;
        if (t.EndsWith("[]", System.StringComparison.Ordinal))
            return IsTranslatableType(t.Substring(0, t.Length - 2), known);

        // Generic wrappers we unwrap — same list the emitter uses.
        var wrappers = new[]
        {
            "PatchField", "System.Nullable",
            "System.Collections.Generic.List", "System.Collections.Generic.IList",
            "System.Collections.Generic.ICollection", "System.Collections.Generic.IEnumerable",
            "System.Collections.Generic.IReadOnlyList", "System.Collections.Generic.IReadOnlyCollection",
            "System.Collections.Generic.HashSet", "System.Collections.Generic.ISet", "System.Collections.Generic.IReadOnlySet",
            "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "HashSet", "ISet", "IReadOnlySet",
            "System.Collections.Generic.Dictionary", "System.Collections.Generic.IDictionary", "System.Collections.Generic.IReadOnlyDictionary",
            "Dictionary", "IDictionary", "IReadOnlyDictionary",
        };
        foreach (var w in wrappers)
        {
            var prefix = w + "<";
            if (!t.StartsWith(prefix, System.StringComparison.Ordinal) || !t.EndsWith(">", System.StringComparison.Ordinal))
                continue;
            var inner = t.Substring(prefix.Length, t.Length - prefix.Length - 1);
            // Split on commas at depth 0 for Dictionary<K, V> — each arg must be translatable.
            foreach (var arg in SplitTopLevelGenericArgs(inner))
                if (!IsTranslatableType(arg.Trim(), known)) return false;
            return true;
        }
        return false;
    }

    private static IEnumerable<string> SplitTopLevelGenericArgs(string inner)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            var ch = inner[i];
            if (ch == '<') depth++;
            else if (ch == '>') depth--;
            else if (ch == ',' && depth == 0)
            {
                yield return inner.Substring(start, i - start);
                start = i + 1;
            }
        }
        yield return inner.Substring(start);
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
