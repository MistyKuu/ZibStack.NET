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
        // Pull every type declaration that carries [GenerateTypes].
        var classes = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or EnumDeclarationSyntax,
            transform: static (ctx, _) =>
            {
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol;
                if (symbol is null) return null;
                if (!SchemaParser.HasGenerateTypes(symbol)) return null;
                return symbol;
            })
            .Where(static s => s is not null);

        var compilation = context.CompilationProvider;

        var combined = classes.Collect().Combine(compilation);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var (symbols, compilation) = source;
            if (symbols.IsDefaultOrEmpty) return;

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
                }
            }

            if (model.Classes.Count == 0 && model.Enums.Count == 0) return;

            var settings = config?.Settings ?? new GlobalSettings();

            // Run each requested emitter.
            var allFiles = new List<EmittedFile>();
            if (RequestsTarget(model, TypeTarget.TypeScript))
                allFiles.AddRange(TypeScriptEmitter.Emit(model, settings));
            if (RequestsTarget(model, TypeTarget.OpenApi))
                allFiles.AddRange(OpenApiEmitter.Emit(model, settings));

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
