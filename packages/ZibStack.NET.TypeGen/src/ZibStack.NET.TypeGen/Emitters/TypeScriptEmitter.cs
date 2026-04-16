using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZibStack.NET.TypeGen.Generator;

/// <summary>
/// Produces TypeScript source from a <see cref="SchemaModel"/>. Pure transformation —
/// no I/O. Output is returned as a list of <see cref="EmittedFile"/> records that
/// the pipeline writes through the obj/generated → MSBuild-task path.
/// </summary>
internal static class TypeScriptEmitter
{
    public static IReadOnlyList<EmittedFile> Emit(SchemaModel model, GlobalSettings settings)
    {
        var files = new List<EmittedFile>();
        var ts = settings.TypeScript;

        // First pass: compute final emitted name per class (apply StripSuffixes etc.).
        // Stored back on the model so the type-reference resolver can look up by source name.
        foreach (var cls in model.Classes)
        {
            if (cls.TsIgnore || (cls.Targets & TypeTarget.TypeScript) == 0) continue;
            cls.EmittedName = ResolveTsTypeName(cls.SourceName, cls.TsNameOverride, ts);
        }
        foreach (var en in model.Enums)
        {
            if (en.TsIgnore || (en.Targets & TypeTarget.TypeScript) == 0) continue;
            en.EmittedName = ResolveTsTypeName(en.SourceName, en.TsNameOverride, ts);
        }

        // Build a C#-fullname → TS-name lookup so property references resolve correctly.
        var tsNameByCSharp = new Dictionary<string, string>();
        foreach (var c in model.Classes)
            if (!c.TsIgnore && (c.Targets & TypeTarget.TypeScript) != 0)
                tsNameByCSharp[c.CSharpFullName] = c.EmittedName;
        foreach (var e in model.Enums)
            if (!e.TsIgnore && (e.Targets & TypeTarget.TypeScript) != 0)
                tsNameByCSharp[e.CSharpFullName] = e.EmittedName;

        if (ts.FileLayout == TypeScriptFileLayout.SingleFile)
        {
            var sb = new StringBuilder();
            EmitBanner(sb, ts);
            foreach (var cls in model.Classes)
                EmitClass(sb, cls, ts, tsNameByCSharp);
            foreach (var en in model.Enums)
                EmitEnum(sb, en, ts);
            files.Add(new EmittedFile(
                Target: TypeTarget.TypeScript,
                OutputDir: ResolveOutputDir(ts.OutputDir, model),
                FileName: ts.SingleFileName,
                Content: sb.ToString()));
        }
        else
        {
            foreach (var cls in model.Classes)
            {
                if (cls.TsIgnore || (cls.Targets & TypeTarget.TypeScript) == 0) continue;
                var sb = new StringBuilder();
                EmitBanner(sb, ts);
                EmitImports(sb, CollectClassReferences(cls, tsNameByCSharp), cls.EmittedName);
                EmitClass(sb, cls, ts, tsNameByCSharp);
                files.Add(new EmittedFile(
                    Target: TypeTarget.TypeScript,
                    OutputDir: cls.OutputDir,
                    FileName: cls.EmittedName + ".ts",
                    Content: sb.ToString()));
            }
            foreach (var en in model.Enums)
            {
                if (en.TsIgnore || (en.Targets & TypeTarget.TypeScript) == 0) continue;
                var sb = new StringBuilder();
                EmitBanner(sb, ts);
                EmitEnum(sb, en, ts);
                files.Add(new EmittedFile(
                    Target: TypeTarget.TypeScript,
                    OutputDir: en.OutputDir,
                    FileName: en.EmittedName + ".ts",
                    Content: sb.ToString()));
            }
        }

        return files;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static void EmitImports(StringBuilder sb, IEnumerable<string> refs, string selfName)
    {
        var sorted = refs.Where(r => r != selfName).Distinct().OrderBy(r => r, System.StringComparer.Ordinal).ToList();
        if (sorted.Count == 0) return;
        foreach (var r in sorted)
            sb.AppendLine($"import {{ {r} }} from './{r}';");
        sb.AppendLine();
    }

    private static HashSet<string> CollectClassReferences(SchemaClass cls, IReadOnlyDictionary<string, string> nameLookup)
    {
        var acc = new HashSet<string>();
        // Pick up the base class so FilePerClass mode imports it for `extends`.
        if (cls.BaseClassFullName is { } bfn && nameLookup.TryGetValue(bfn, out var bn))
            acc.Add(bn);
        foreach (var prop in cls.Properties)
        {
            if (prop.TsIgnore) continue;
            // Explicit TsType override is an opaque literal — user owns any imports themselves.
            if (prop.TsTypeOverride != null) continue;
            CollectRefs(prop.CSharpTypeFullName, nameLookup, acc);
        }
        return acc;
    }

    private static void CollectRefs(string cSharpType, IReadOnlyDictionary<string, string> nameLookup, HashSet<string> acc)
    {
        var t = cSharpType.TrimEnd('?');
        var patchInner = ExtractGeneric(t, "PatchField");
        if (patchInner != null) { CollectRefs(patchInner, nameLookup, acc); return; }
        if (nameLookup.TryGetValue(t, out var mapped)) { acc.Add(mapped); return; }
        if (t.EndsWith("[]")) { CollectRefs(t.Substring(0, t.Length - 2), nameLookup, acc); return; }
        var listInner = ExtractGeneric(t, "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection");
        if (listInner != null) { CollectRefs(listInner, nameLookup, acc); return; }
        var dict = ExtractTwoGenericArgs(t, "Dictionary", "IDictionary", "IReadOnlyDictionary");
        if (dict != null) { CollectRefs(dict.Value.K, nameLookup, acc); CollectRefs(dict.Value.V, nameLookup, acc); return; }
    }

    private static void EmitBanner(StringBuilder sb, TypeScriptSettings ts)
    {
        if (!ts.EmitGeneratedBanner) return;
        sb.AppendLine("// @generated by ZibStack.NET.TypeGen — do not edit");
        sb.AppendLine();
    }

    private static void EmitClass(StringBuilder sb, SchemaClass cls, TypeScriptSettings ts, IReadOnlyDictionary<string, string> nameLookup)
    {
        if (cls.TsIgnore || (cls.Targets & TypeTarget.TypeScript) == 0) return;
        // Only express inheritance when the base is in the emit set — otherwise its
        // properties were pre-inlined by the parser and there's nothing to extend.
        var baseTs = cls.BaseClassFullName is { } bfn && nameLookup.TryGetValue(bfn, out var bn) ? bn : null;

        if (ts.UseInterfaces)
            sb.AppendLine(baseTs is null
                ? $"export interface {cls.EmittedName} {{"
                : $"export interface {cls.EmittedName} extends {baseTs} {{");
        else
            sb.AppendLine(baseTs is null
                ? $"export type {cls.EmittedName} = {{"
                : $"export type {cls.EmittedName} = {baseTs} & {{");

        foreach (var prop in cls.Properties)
        {
            if (prop.TsIgnore) continue;
            var name = prop.TsNameOverride ?? ApplyNameStyle(prop.SourceName, ts.PropertyNameStyle);
            var typeExpr = prop.TsTypeOverride ?? MapCSharpToTs(prop.CSharpTypeFullName, prop.IsNullable, nameLookup);
            var optionalMarker = prop.IsNullable ? "?" : "";
            sb.AppendLine($"    {name}{optionalMarker}: {typeExpr};");
        }

        // [JsonExtensionData] property → TS index signature catching unmapped keys.
        // Always emit as `unknown` (or `unknown | V` when value type is constrained)
        // to stay compatible with the named properties above — strict mode rejects
        // an index signature whose value isn't a supertype of every named field.
        if (cls.AllowsAdditionalProperties)
        {
            var valueType = "unknown";
            if (cls.AdditionalPropertiesValueCSharpType is not null)
                valueType = MapCSharpToTs(cls.AdditionalPropertiesValueCSharpType, isNullable: false, nameLookup) + " | unknown";
            sb.AppendLine($"    [key: string]: {valueType};");
        }

        sb.AppendLine(ts.UseInterfaces ? "}" : "};");
        sb.AppendLine();
    }

    private static void EmitEnum(StringBuilder sb, SchemaEnum en, TypeScriptSettings ts)
    {
        if (en.TsIgnore || (en.Targets & TypeTarget.TypeScript) == 0) return;
        sb.AppendLine($"export enum {en.EmittedName} {{");
        foreach (var m in en.Members)
            sb.AppendLine($"    {m.Name} = {m.Value},");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Maps a C# type display string to a TypeScript type expression. Falls back to
    /// <c>any</c> for unknown / unsupported types — those are surfaced separately
    /// by the analyzer (TG0002) so the developer sees a warning at the call site.
    /// </summary>
    private static string MapCSharpToTs(string cSharpType, bool isNullable, IReadOnlyDictionary<string, string> nameLookup)
    {
        // Strip nullable annotation marker if present.
        var t = cSharpType.TrimEnd('?');

        // Unwrap Dto's PatchField<T> tri-state wrapper — TS consumers see plain T.
        var patchInner = ExtractGeneric(t, "PatchField");
        if (patchInner != null) return MapCSharpToTs(patchInner, isNullable, nameLookup);

        // Direct user-DTO reference?
        if (nameLookup.TryGetValue(t, out var mapped)) return mapped;

        // Collections: List<T>, IList<T>, IEnumerable<T>, T[]
        if (t.EndsWith("[]"))
            return MapCSharpToTs(t.Substring(0, t.Length - 2), false, nameLookup) + "[]";
        var listMatch = ExtractGeneric(t, "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection");
        if (listMatch != null)
            return MapCSharpToTs(listMatch, false, nameLookup) + "[]";

        // Dictionary<K, V> → Record<K, V> (keys must be string|number)
        var dictMatch = ExtractTwoGenericArgs(t, "Dictionary", "IDictionary", "IReadOnlyDictionary");
        if (dictMatch != null)
            return $"Record<{MapCSharpToTs(dictMatch.Value.K, false, nameLookup)}, {MapCSharpToTs(dictMatch.Value.V, false, nameLookup)}>";

        return t switch
        {
            "string" => "string",
            "bool" or "System.Boolean" => "boolean",
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                or "float" or "double" or "System.Int32" or "System.Int64" or "System.Single" or "System.Double" => "number",
            "decimal" or "System.Decimal" => "string",   // safest: decimals don't fit in number
            "System.Guid" or "Guid" => "string",
            "System.DateTime" or "DateTime" or "System.DateTimeOffset" or "DateTimeOffset" => "string",   // ISO 8601
            "System.DateOnly" or "DateOnly" or "System.TimeOnly" or "TimeOnly" or "System.TimeSpan" or "TimeSpan" => "string",
            "object" => "unknown",
            _ => "unknown",
        };
    }

    private static string? ExtractGeneric(string typeName, params string[] names)
    {
        foreach (var n in names)
        {
            // Match "List<T>" or "System.Collections.Generic.List<T>"
            var idx = typeName.IndexOf(n + "<", System.StringComparison.Ordinal);
            if (idx < 0) continue;
            // Ensure it's the type part, not a substring like "MyList<T>"
            if (idx > 0 && char.IsLetterOrDigit(typeName[idx - 1])) continue;
            var open = idx + n.Length;
            var inner = ExtractBalanced(typeName, open);
            if (inner != null) return inner;
        }
        return null;
    }

    private static (string K, string V)? ExtractTwoGenericArgs(string typeName, params string[] names)
    {
        var inner = ExtractGeneric(typeName, names);
        if (inner is null) return null;
        // Split top-level comma only.
        int depth = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '<') depth++;
            else if (inner[i] == '>') depth--;
            else if (inner[i] == ',' && depth == 0)
                return (inner.Substring(0, i).Trim(), inner.Substring(i + 1).Trim());
        }
        return null;
    }

    private static string? ExtractBalanced(string s, int openAngleBracketIndex)
    {
        if (openAngleBracketIndex >= s.Length || s[openAngleBracketIndex] != '<') return null;
        int depth = 1;
        for (int i = openAngleBracketIndex + 1; i < s.Length; i++)
        {
            if (s[i] == '<') depth++;
            else if (s[i] == '>') { depth--; if (depth == 0) return s.Substring(openAngleBracketIndex + 1, i - openAngleBracketIndex - 1); }
        }
        return null;
    }

    private static string ApplyNameStyle(string name, NameStyle style)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return style switch
        {
            NameStyle.AsIs => name,
            NameStyle.CamelCase => char.ToLowerInvariant(name[0]) + name.Substring(1),
            NameStyle.PascalCase => char.ToUpperInvariant(name[0]) + name.Substring(1),
            NameStyle.SnakeCase => ToSeparated(name, '_'),
            NameStyle.KebabCase => ToSeparated(name, '-'),
            _ => name,
        };
    }

    private static string ToSeparated(string name, char sep)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append(sep);
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    private static string ResolveTsTypeName(string source, string? overrideName, TypeScriptSettings ts)
    {
        if (overrideName != null) return overrideName;
        var n = source;
        // Strip configured suffixes (longest first to avoid partial-strip ordering issues).
        foreach (var suffix in ts.StripSuffixes.OrderByDescending(s => s.Length))
        {
            if (n.EndsWith(suffix, System.StringComparison.Ordinal) && n.Length > suffix.Length)
            {
                n = n.Substring(0, n.Length - suffix.Length);
                break;
            }
        }
        return ApplyNameStyle(n, ts.TypeNameStyle);
    }

    private static string ResolveOutputDir(string? globalDir, SchemaModel model)
    {
        // Prefer global setting, else first class's per-attribute OutputDir, else ".".
        if (!string.IsNullOrEmpty(globalDir)) return globalDir!;
        var first = model.Classes.FirstOrDefault();
        return first?.OutputDir ?? ".";
    }
}

/// <summary>
/// One emitted file. The pipeline serializes these into a manifest the MSBuild
/// task reads post-build to write to the user's filesystem.
/// </summary>
internal sealed record EmittedFile(TypeTarget Target, string OutputDir, string FileName, string Content);
