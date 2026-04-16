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
            // Roll up user-supplied imports from every emitted class so the single
            // file gets one import block at the top. Exclude any symbol that is ALSO
            // emitted into this file — an auto-computed [TsType<T>] importFrom for a
            // locally-generated T is redundant in SingleFile mode (the type's
            // definition lives a few lines down in the same file, no module boundary).
            var localTsNames = new HashSet<string>(
                model.Classes.Where(c => !c.TsIgnore && (c.Targets & TypeTarget.TypeScript) != 0)
                    .Select(c => c.TsNameOverride ?? c.EmittedName)
                .Concat(model.Enums.Where(e => !e.TsIgnore && (e.Targets & TypeTarget.TypeScript) != 0)
                    .Select(e => e.TsNameOverride ?? e.EmittedName)),
                System.StringComparer.Ordinal);
            var rolledImports = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
            foreach (var cls in model.Classes)
            {
                if (cls.TsIgnore || (cls.Targets & TypeTarget.TypeScript) == 0) continue;
                foreach (var kvp in CollectUserImports(cls))
                {
                    var external = kvp.Value.Where(n => !localTsNames.Contains(n)).ToList();
                    if (external.Count == 0) continue;
                    if (!rolledImports.TryGetValue(kvp.Key, out var names))
                        rolledImports[kvp.Key] = names = new HashSet<string>(System.StringComparer.Ordinal);
                    foreach (var n in external) names.Add(n);
                }
            }
            EmitImports(sb, System.Linq.Enumerable.Empty<string>(), selfName: "", rolledImports);
            foreach (var cls in model.Classes)
                EmitClass(sb, cls, ts, tsNameByCSharp, model);
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
                EmitImports(sb, CollectClassReferences(cls, tsNameByCSharp), cls.EmittedName, CollectUserImports(cls));
                EmitClass(sb, cls, ts, tsNameByCSharp, model);
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

    private static void EmitImports(StringBuilder sb, IEnumerable<string> refs, string selfName,
        IReadOnlyDictionary<string, HashSet<string>>? userImports = null)
    {
        var sorted = refs.Where(r => r != selfName).Distinct().OrderBy(r => r, System.StringComparer.Ordinal).ToList();
        bool any = false;
        foreach (var r in sorted)
        {
            sb.AppendLine($"import {{ {r} }} from './{r}';");
            any = true;
        }
        if (userImports is not null)
        {
            foreach (var kvp in userImports.OrderBy(k => k.Key, System.StringComparer.Ordinal))
            {
                if (kvp.Value.Count == 0) continue;
                var names = string.Join(", ", kvp.Value.OrderBy(n => n, System.StringComparer.Ordinal));
                sb.AppendLine($"import {{ {names} }} from '{kvp.Key}';");
                any = true;
            }
        }
        if (any) sb.AppendLine();
    }

    private static HashSet<string> CollectClassReferences(SchemaClass cls, IReadOnlyDictionary<string, string> nameLookup)
    {
        var acc = new HashSet<string>();
        // Pick up the base class so FilePerClass mode imports it for `extends`.
        if (cls.BaseClassFullName is { } bfn && nameLookup.TryGetValue(bfn, out var bn))
            acc.Add(bn);
        // Generic base args (e.g. `extends Base<SomeType>` → SomeType import).
        foreach (var arg in cls.BaseClassTypeArguments)
            CollectRefs(arg, nameLookup, acc);
        // Implemented interfaces each need their own import line when emitted in
        // FilePerClass mode — the `extends` chain references them by name.
        for (int i = 0; i < cls.ImplementedInterfaces.Count; i++)
        {
            var ifaceFqn = cls.ImplementedInterfaces[i];
            if (nameLookup.TryGetValue(ifaceFqn, out var ifaceName)) acc.Add(ifaceName);
            foreach (var arg in cls.ImplementedInterfaceTypeArguments[i])
                CollectRefs(arg, nameLookup, acc);
        }
        foreach (var prop in cls.Properties)
        {
            if (prop.TsIgnore) continue;
            // Explicit TsType override is an opaque literal — user-imports handled separately.
            if (prop.TsTypeOverride != null) continue;
            CollectRefs(prop.CSharpTypeFullName, nameLookup, acc);
        }
        return acc;
    }

    /// <summary>
    /// Builds the per-file map of user-supplied imports from <c>[TsType("Foo",
    /// ImportFrom = "./bar")]</c> annotations. Identifiers are extracted from the
    /// type expression by matching PascalCase tokens (skips primitives like
    /// <c>string</c>, <c>number</c>, literal strings, etc.). Multiple properties
    /// pointing at the same path get merged into one import line.
    /// </summary>
    private static Dictionary<string, HashSet<string>> CollectUserImports(SchemaClass cls)
    {
        var byPath = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
        foreach (var prop in cls.Properties)
        {
            if (prop.TsIgnore) continue;
            if (prop.TsTypeOverride is null || string.IsNullOrEmpty(prop.TsImportFrom)) continue;
            if (!byPath.TryGetValue(prop.TsImportFrom!, out var names))
                byPath[prop.TsImportFrom!] = names = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var ident in ExtractPascalIdentifiers(prop.TsTypeOverride))
                names.Add(ident);
        }
        return byPath;
    }

    private static IEnumerable<string> ExtractPascalIdentifiers(string typeExpression)
    {
        // PascalCase identifiers — covers TS class/interface/type names. Skips
        // primitives (`string`, `number`, `boolean`), literals (`'pending'`), `null`,
        // `undefined`, etc. Caller dedupes via HashSet.
        return System.Text.RegularExpressions.Regex.Matches(typeExpression, @"[A-Z][A-Za-z0-9_]*")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Value);
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

    private static void EmitClass(StringBuilder sb, SchemaClass cls, TypeScriptSettings ts, IReadOnlyDictionary<string, string> nameLookup, SchemaModel model)
    {
        if (cls.TsIgnore || (cls.Targets & TypeTarget.TypeScript) == 0) return;

        // Polymorphic base → `type Base = V1 | V2 | …;` discriminated union.
        // The variants still emit as ordinary interfaces (later in the same file
        // or in siblings); the base itself is a union, not a struct-shape.
        if (cls.PolymorphicVariants.Count > 0 && cls.PolymorphicDiscriminator is not null)
        {
            var variantTsNames = cls.PolymorphicVariants
                .Select(v => nameLookup.TryGetValue(v.CSharpFullName, out var n) ? n : null)
                .Where(n => n is not null)
                .ToList();
            if (variantTsNames.Count > 0)
            {
                sb.AppendLine($"export type {cls.EmittedName} = {string.Join(" | ", variantTsNames)};");
                sb.AppendLine();
                return;
            }
        }

        // Only express inheritance when the base is in the emit set — otherwise its
        // properties were pre-inlined by the parser and there's nothing to extend.
        // When the base is a polymorphic union (emitted as `type Base = V1 | …`),
        // DON'T extend it — the variant is one of the union members, not a subtype
        // of a struct. Instead the discriminator is stamped as a literal property
        // below.
        string? baseTs = null;
        if (cls.BaseClassFullName is { } bfn && nameLookup.TryGetValue(bfn, out var bn))
        {
            var isBasePolymorphic = cls.PolymorphicDiscriminatorValue is not null;
            if (!isBasePolymorphic) baseTs = bn;
        }

        // Generic class header: `interface Foo<T, U>`. Type-parameter symbols
        // come straight from the open generic definition — one letter per slot.
        var typeParamsSuffix = cls.TypeParameters.Count > 0
            ? "<" + string.Join(", ", cls.TypeParameters) + ">"
            : "";

        // Constructed base: `extends Base<SomeType>`. Args get mapped like any
        // other C# type so enum / user DTO / collection args render correctly.
        var baseSuffix = "";
        if (baseTs is not null && cls.BaseClassTypeArguments.Count > 0)
        {
            var args = cls.BaseClassTypeArguments
                .Select(a => MapCSharpToTs(a, isNullable: false, nameLookup, cls.TypeParameters));
            baseSuffix = "<" + string.Join(", ", args) + ">";
        }

        // Build the TS extends list: base class first, then implemented interfaces
        // that survive the TS-visibility gate (present in model, not TsIgnored,
        // targeted for TypeScript). Interfaces that fail the gate stay out — the
        // class keeps its own redeclaration of their members below instead.
        var extendsParts = new List<string>();
        if (baseTs is not null) extendsParts.Add(baseTs + baseSuffix);
        var coveredMemberNames = new HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 0; i < cls.ImplementedInterfaces.Count; i++)
        {
            var ifaceFqn = cls.ImplementedInterfaces[i];
            var ifaceCls = model.Classes.FirstOrDefault(c => c.CSharpFullName == ifaceFqn);
            if (ifaceCls is null) continue;
            if (ifaceCls.TsIgnore || (ifaceCls.Targets & TypeTarget.TypeScript) == 0) continue;
            if (!nameLookup.TryGetValue(ifaceFqn, out var ifaceName)) continue;

            var argList = cls.ImplementedInterfaceTypeArguments[i];
            var ifaceSuffix = argList.Count == 0 ? "" :
                "<" + string.Join(", ", argList.Select(a => MapCSharpToTs(a, false, nameLookup, cls.TypeParameters))) + ">";
            extendsParts.Add(ifaceName + ifaceSuffix);

            // Track member names that the interface covers — those get skipped
            // in the class's own property loop so we don't redeclare them.
            foreach (var p in ifaceCls.Properties)
                coveredMemberNames.Add(p.SourceName);
        }
        // Base-class members already contribute via existing "emittedAncestorNames"
        // dedupe done at parse time (cls.Properties are the declared-minus-inherited
        // set). Interfaces weren't visible then; cover them here.

        var extendsClause = extendsParts.Count > 0 ? " extends " + string.Join(", ", extendsParts) : "";

        if (cls.IsInterface || ts.UseInterfaces)
            sb.AppendLine($"export interface {cls.EmittedName}{typeParamsSuffix}{extendsClause} {{");
        else
        {
            // `type` aliases don't support `extends` — use an intersection block.
            var intersectPrefix = extendsParts.Count > 0
                ? string.Join(" & ", extendsParts) + " & "
                : "";
            sb.AppendLine($"export type {cls.EmittedName}{typeParamsSuffix} = {intersectPrefix}{{");
        }

        // Polymorphic variant: stamp the discriminator property as a literal type
        // FIRST so narrowing works (`if (s.kind === "circle") …` → TS sees Circle).
        if (cls.PolymorphicDiscriminatorValue is not null
            && cls.PolymorphicDiscriminatorPropertyOnVariant is { } discName)
        {
            sb.AppendLine($"    {discName}: \"{cls.PolymorphicDiscriminatorValue}\";");
        }

        foreach (var prop in cls.Properties)
        {
            if (prop.TsIgnore) continue;
            // Skip properties already carried by an extended interface — avoids
            // duplicate declarations that'd widen the required set and confuse
            // structural type compatibility on `extends` chains.
            if (coveredMemberNames.Contains(prop.SourceName)) continue;
            var name = prop.TsNameOverride ?? ApplyNameStyle(prop.SourceName, ts.PropertyNameStyle);
            var typeExpr = prop.TsTypeOverride ?? MapCSharpToTs(prop.CSharpTypeFullName, prop.IsNullable, nameLookup, cls.TypeParameters);
            // Server-computed getters (`public int X => …;` / `{ get; private set; }`)
            // are `readonly` (blocks client reassignment) AND optional: a single
            // interface serves both read and write paths, so forcing the field at
            // construction time would break client-side object building for
            // create/update payloads. On the response side the server always
            // returns the value — consumers read it as usual. `init` accessors
            // stay plain-required: the Dto pipeline already excludes them from
            // the Update request, and at construction time they ARE required.
            var readOnlyMod = prop.IsReadOnly ? "readonly " : "";
            // Optional marker: NRT-nullable OR server-computed readonly → optional.
            // But `[Required]` / `[ZRequired]` / C# `required` keyword override NRT
            // nullability — client must always send this field even if the type is
            // `string?`. Matches the wire-level contract enforced by validators.
            var effectivelyNullable = prop.IsNullable && !prop.IsExplicitlyRequired;
            var optionalMarker = (effectivelyNullable || prop.IsReadOnly) ? "?" : "";
            sb.AppendLine($"    {readOnlyMod}{name}{optionalMarker}: {typeExpr};");
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

        // Match the closing brace to the opening form — an interface-kind schema
        // was forced to emit as `interface {` above; everything else respects
        // the user's UseInterfaces preference.
        var emittedAsInterface = cls.IsInterface || ts.UseInterfaces;
        sb.AppendLine(emittedAsInterface ? "}" : "};");
        sb.AppendLine();
    }

    private static void EmitEnum(StringBuilder sb, SchemaEnum en, TypeScriptSettings ts)
    {
        if (en.TsIgnore || (en.Targets & TypeTarget.TypeScript) == 0) return;

        // Union form applies only to string-serialized enums — numeric unions of literals
        // (`0 | 1 | 2`) are less useful and break existing iteration patterns. Numeric
        // enums always emit as `export enum` regardless of ts.EnumStyle.
        if (en.IsStringSerialized && ts.EnumStyle == TsEnumStyle.Union)
        {
            var literals = string.Join(" | ", en.Members.Select(m => $"\"{m.Name}\""));
            sb.AppendLine($"export type {en.EmittedName} = {literals};");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"export enum {en.EmittedName} {{");
        foreach (var m in en.Members)
        {
            // String-serialised enums ([JsonStringEnumConverter] etc.) emit member-name
            // string values so JSON parse / stringify round-trips against the TS type.
            // Without the converter the runtime JSON is integers — keep the numeric form.
            var value = en.IsStringSerialized ? $"\"{m.Name}\"" : m.Value.ToString();
            sb.AppendLine($"    {m.Name} = {value},");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Maps a C# type display string to a TypeScript type expression. Falls back to
    /// <c>any</c> for unknown / unsupported types — those are surfaced separately
    /// by the analyzer (TG0002) so the developer sees a warning at the call site.
    /// </summary>
    private static string MapCSharpToTs(string cSharpType, bool isNullable, IReadOnlyDictionary<string, string> nameLookup,
        IReadOnlyList<string>? typeParameters = null)
    {
        // Strip nullable annotation marker if present.
        var t = cSharpType.TrimEnd('?');

        // When emitting inside a generic class, the property type may BE one of
        // the declaring class's type parameters (e.g. `T Payload` on `Base<T>`).
        // Render the parameter name verbatim — no lookup, no style.
        if (typeParameters is not null && typeParameters.Contains(t)) return t;

        // Unwrap Dto's PatchField<T> tri-state wrapper — TS consumers see plain T.
        var patchInner = ExtractGeneric(t, "PatchField");
        if (patchInner != null) return MapCSharpToTs(patchInner, isNullable, nameLookup, typeParameters);

        // Direct user-DTO reference?
        if (nameLookup.TryGetValue(t, out var mapped)) return mapped;

        // Collections: List<T>, IList<T>, IEnumerable<T>, T[]
        if (t.EndsWith("[]"))
            return MapCSharpToTs(t.Substring(0, t.Length - 2), false, nameLookup, typeParameters) + "[]";
        var listMatch = ExtractGeneric(t, "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection");
        if (listMatch != null)
            return MapCSharpToTs(listMatch, false, nameLookup, typeParameters) + "[]";

        // Dictionary<K, V> → Record<K, V> (keys must be string|number)
        var dictMatch = ExtractTwoGenericArgs(t, "Dictionary", "IDictionary", "IReadOnlyDictionary");
        if (dictMatch != null)
            return $"Record<{MapCSharpToTs(dictMatch.Value.K, false, nameLookup, typeParameters)}, {MapCSharpToTs(dictMatch.Value.V, false, nameLookup, typeParameters)}>";

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
