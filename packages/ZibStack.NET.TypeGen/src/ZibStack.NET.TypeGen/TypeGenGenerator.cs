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
            var (symbols, _) = source;
            if (symbols.IsDefaultOrEmpty) return;

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
                    ReportTargetsIfEmpty(spc, sym, en.Targets);
                    ReportInvalidOutputDirIfEmpty(spc, sym, en.OutputDir);
                    model.Enums.Add(en);
                }
                else
                {
                    var cls = SchemaParser.ParseClass(sym);
                    if (cls is null) continue;
                    ReportTargetsIfEmpty(spc, sym, cls.Targets);
                    ReportInvalidOutputDirIfEmpty(spc, sym, cls.OutputDir);
                    model.Classes.Add(cls);
                }
            }

            if (model.Classes.Count == 0 && model.Enums.Count == 0) return;

            // Settings: defaults for now. Configurator parsing comes in a follow-up
            // (memory project_typegen_backlog). The pipeline already flows settings
            // through, so wiring it later is additive.
            var settings = new GlobalSettings();

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
