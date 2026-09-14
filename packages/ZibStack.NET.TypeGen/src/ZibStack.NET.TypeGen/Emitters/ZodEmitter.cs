using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZibStack.NET.TypeGen.Generator;

/// <summary>
/// Produces Zod schema source from a <see cref="SchemaModel"/>. Each class emits
/// <c>export const {Name}Schema = z.object({…})</c> plus an optional
/// <c>export type {Name} = z.infer&lt;typeof {Name}Schema&gt;</c> alias so
/// Zod-only consumers get both runtime validator and structural type.
///
/// <para>
/// Completely independent of the TypeScript emitter — when both targets run
/// in the same project the TS emitter produces <c>Order.ts</c> (interface)
/// while Zod produces <c>Order.schema.ts</c> (schema + inferred type). No
/// cross-file coupling, no name collisions. Drift is structurally impossible
/// because both derive from the same <see cref="SchemaModel"/>.
/// </para>
/// </summary>
internal static class ZodEmitter
{
    public static IReadOnlyList<EmittedFile> Emit(SchemaModel model, GlobalSettings settings)
    {
        var files = new List<EmittedFile>();
        var zs = settings.Zod;

        // Compute emitted (type) name per class/enum — Zod emits its schema under
        // `{EmittedName}{SchemaConstSuffix}` and the inferred type alias under
        // `EmittedName`. Reuse the TS suffix-strip setup for consistency.
        foreach (var cls in model.Classes)
        {
            if (SkipClass(cls)) continue;
            if (string.IsNullOrEmpty(cls.EmittedName))
                cls.EmittedName = ResolveTypeName(cls.SourceName, cls.TsNameOverride);
        }
        foreach (var en in model.Enums)
        {
            if (SkipEnum(en)) continue;
            if (string.IsNullOrEmpty(en.EmittedName))
                en.EmittedName = ResolveTypeName(en.SourceName, en.TsNameOverride);
        }

        // C#-fullname → Zod type name lookup (for cross-schema `$ref`s).
        var nameByCSharp = new Dictionary<string, string>();
        foreach (var c in model.Classes)
            if (!SkipClass(c)) nameByCSharp[c.CSharpFullName] = c.EmittedName;
        foreach (var e in model.Enums)
            if (!SkipEnum(e)) nameByCSharp[e.CSharpFullName] = e.EmittedName;

        if (zs.FileLayout == ZodFileLayout.SingleFile)
        {
            var sb = new StringBuilder();
            EmitBanner(sb, zs);
            sb.AppendLine("import { z } from 'zod';");
            EmitConformanceImports(sb, model.Classes.Where(c => !SkipClass(c) && (c.Targets & TypeTarget.TypeScript) != 0).Select(c => c.EmittedName)
                .Concat(model.Enums.Where(e => !SkipEnum(e) && (e.Targets & TypeTarget.TypeScript) != 0).Select(e => e.EmittedName)),
                ResolveOutputDir(zs.OutputDir, model), settings.TypeScript, zs, model);
            sb.AppendLine();

            // In SingleFile mode order matters — a schema has to be declared
            // BEFORE any other that references it. Topo-sort: enums first (no
            // dependencies among them), then classes in base→derived order with
            // polymorphic variants before their union parents.
            foreach (var en in model.Enums)
                EmitEnum(sb, en, zs);
            foreach (var cls in TopoSortClasses(model))
                EmitClass(sb, cls, zs, nameByCSharp, model);

            files.Add(new EmittedFile(
                Target: TypeTarget.Zod,
                OutputDir: ResolveOutputDir(zs.OutputDir, model),
                FileName: zs.SingleFileName,
                Content: sb.ToString()));
        }
        else
        {
            var globalZodDir = zs.OutputDir;
            foreach (var cls in model.Classes)
            {
                if (SkipClass(cls)) continue;
                var sb = new StringBuilder();
                EmitBanner(sb, zs);
                sb.AppendLine("import { z } from 'zod';");
                var outputDir = (cls.HasExplicitOutputDir ? cls.OutputDir : !string.IsNullOrEmpty(globalZodDir) ? globalZodDir : cls.OutputDir) ?? ".";
                EmitConformanceImports(sb, (cls.Targets & TypeTarget.TypeScript) != 0 ? new[] { cls.EmittedName } : System.Array.Empty<string>(), outputDir, settings.TypeScript, zs, model);
                EmitImports(sb, CollectClassReferences(cls, nameByCSharp), cls.EmittedName, zs);
                sb.AppendLine();
                EmitClass(sb, cls, zs, nameByCSharp, model);
                files.Add(new EmittedFile(
                    Target: TypeTarget.Zod,
                    OutputDir: outputDir,
                    FileName: cls.EmittedName + zs.FileSuffix + ".ts",
                    Content: sb.ToString()));
            }
            foreach (var en in model.Enums)
            {
                if (SkipEnum(en)) continue;
                var sb = new StringBuilder();
                EmitBanner(sb, zs);
                sb.AppendLine("import { z } from 'zod';");
                var outputDir = (en.HasExplicitOutputDir ? en.OutputDir : !string.IsNullOrEmpty(globalZodDir) ? globalZodDir : en.OutputDir) ?? ".";
                EmitConformanceImports(sb, (en.Targets & TypeTarget.TypeScript) != 0 ? new[] { en.EmittedName } : System.Array.Empty<string>(), outputDir, settings.TypeScript, zs, model);
                sb.AppendLine();
                EmitEnum(sb, en, zs);
                files.Add(new EmittedFile(
                    Target: TypeTarget.Zod,
                    OutputDir: outputDir,
                    FileName: en.EmittedName + zs.FileSuffix + ".ts",
                    Content: sb.ToString()));
            }
        }

        return files;
    }

    // ── skip predicates ─────────────────────────────────────────────────────

    private static bool SkipClass(SchemaClass cls) =>
        cls.TsIgnore || (cls.Targets & TypeTarget.Zod) == 0;

    private static bool SkipEnum(SchemaEnum en) =>
        en.TsIgnore || (en.Targets & TypeTarget.Zod) == 0;

    // ── core emit ───────────────────────────────────────────────────────────

    private static void EmitBanner(StringBuilder sb, ZodSettings zs)
    {
        if (!zs.EmitGeneratedBanner) return;
        sb.AppendLine("// @generated by ZibStack.NET.TypeGen — do not edit");
    }

    private static void EmitImports(StringBuilder sb, IEnumerable<string> refs, string selfName, ZodSettings zs)
    {
        var sorted = refs.Where(r => r != selfName).Distinct().OrderBy(r => r, System.StringComparer.Ordinal).ToList();
        foreach (var r in sorted)
            sb.AppendLine($"import {{ {r}{zs.SchemaConstSuffix} }} from './{r}{zs.FileSuffix}';");
    }

    private static void EmitConformanceImports(
        StringBuilder sb,
        IEnumerable<string> typeNames,
        string zodOutputDir,
        TypeScriptSettings ts,
        ZodSettings zs,
        SchemaModel model)
    {
        if (!model.Classes.Any() && !model.Enums.Any()) return;
        if (!zs.ConformToTypeScriptTypes) return;

        var names = typeNames.Distinct().OrderBy(n => n, System.StringComparer.Ordinal).ToList();
        if (names.Count == 0) return;
        var tsOutputDir = !string.IsNullOrEmpty(ts.OutputDir)
            ? ts.OutputDir!
            : model.Classes.FirstOrDefault()?.OutputDir ?? model.Enums.FirstOrDefault()?.OutputDir ?? ".";
        if (ts.FileLayout == TypeScriptFileLayout.SingleFile)
        {
            var file = StripTsExtension(string.IsNullOrWhiteSpace(ts.SingleFileName) ? "models.ts" : ts.SingleFileName);
            var path = SchemaParser.ComputeRelativeImport(zodOutputDir, tsOutputDir, file);
            sb.AppendLine($"import type {{ {string.Join(", ", names)} }} from '{path}';");
            return;
        }

        foreach (var name in names)
        {
            var matchingClass = model.Classes.FirstOrDefault(c => c.EmittedName == name || c.TypeScriptEmittedName == name);
            var matchingEnum = model.Enums.FirstOrDefault(e => e.EmittedName == name || e.TypeScriptEmittedName == name);
            var perTypeDir = matchingClass is not null
                ? matchingClass.HasExplicitOutputDir ? matchingClass.OutputDir : !string.IsNullOrEmpty(ts.OutputDir) ? ts.OutputDir! : matchingClass.OutputDir
                : matchingEnum is not null
                    ? matchingEnum.HasExplicitOutputDir ? matchingEnum.OutputDir : !string.IsNullOrEmpty(ts.OutputDir) ? ts.OutputDir! : matchingEnum.OutputDir
                    : tsOutputDir;
            var path = SchemaParser.ComputeRelativeImport(zodOutputDir, perTypeDir ?? ".", name);
            sb.AppendLine($"import type {{ {name} }} from '{path}';");
        }
    }

    private static void EmitClass(
        StringBuilder sb,
        SchemaClass cls,
        ZodSettings zs,
        IReadOnlyDictionary<string, string> nameByCSharp,
        SchemaModel model)
    {
        if (SkipClass(cls)) return;

        var schemaConst = cls.EmittedName + zs.SchemaConstSuffix;
        var conform = zs.ConformToTypeScriptTypes && (cls.Targets & TypeTarget.TypeScript) != 0;
        var lazySchemaNames = CollectLazySchemaNames(cls, model, nameByCSharp);
        var typeAnnotation = lazySchemaNames.Count > 0
            ? conform ? $": z.ZodType<{cls.EmittedName}>" : ": z.ZodType<any>"
            : "";
        var wrappers = (zs.Compilation == ZodCompilationMode.Compile ? 1 : 0)
            + (conform ? 1 : 0);
        var initializerPrefix = (zs.Compilation == ZodCompilationMode.Compile ? "z.compile(" : "")
            + (conform ? $"z.toZod<{cls.EmittedName}>()(" : "");
        var initializerSuffix = new string(')', wrappers);

        // Polymorphic base → z.discriminatedUnion("kind", [VariantASchema, …]).
        // Zod's discriminatedUnion gives exhaustive narrowing from the literal
        // discriminator, matching the TS `type Base = A | B;` + literal-key
        // narrowing the TypeScript emitter produces.
        if (cls.PolymorphicVariants.Count > 0 && cls.PolymorphicDiscriminator is not null)
        {
            var variantSchemas = cls.PolymorphicVariants
                .Select(v => nameByCSharp.TryGetValue(v.CSharpFullName, out var n) ? n + zs.SchemaConstSuffix : null)
                .Where(n => n is not null)
                .ToList();
            if (variantSchemas.Count > 0)
            {
                sb.AppendLine($"export const {schemaConst}{typeAnnotation} = {initializerPrefix}z.discriminatedUnion('{cls.PolymorphicDiscriminator}', [");
                for (int i = 0; i < variantSchemas.Count; i++)
                {
                    var comma = i < variantSchemas.Count - 1 ? "," : "";
                    sb.AppendLine($"    {variantSchemas[i]}{comma}");
                }
                sb.AppendLine($"]){initializerSuffix};");
                if (zs.EmitInferredTypes && !conform)
                {
                    sb.AppendLine($"export type {cls.EmittedName} = z.infer<typeof {schemaConst}>;");
                }
                sb.AppendLine();
                return;
            }
        }

        // Inheritance: when the base class is in the emit set, compose
        // `BaseSchema.extend({ …own fields })`. Otherwise the base's properties
        // were pre-inlined by the parser and the shape already contains them.
        // When the base is a polymorphic union (discriminatedUnion), DON'T extend —
        // the variant is a union member, not a subtype of a struct.
        string? baseSchema = null;
        if (cls.BaseClassFullName is { } bfn && nameByCSharp.TryGetValue(bfn, out var baseName))
        {
            var baseClass = model.Classes.FirstOrDefault(c => c.CSharpFullName == bfn);
            var baseIsUnion = baseClass is { PolymorphicVariants.Count: > 0 };
            if (!baseIsUnion)
                baseSchema = baseName + zs.SchemaConstSuffix;
        }

        // Interfaces: each implemented interface present in the model merges
        // into the shape via `.extend({...})` chain. Mirrors the TS
        // `extends I1, I2` chain — but in Zod the composition is explicit on
        // the schema, not the inferred type.
        var ifaceSchemas = new List<string>();
        for (int i = 0; i < cls.ImplementedInterfaces.Count; i++)
        {
            var ifaceFqn = cls.ImplementedInterfaces[i];
            var ifaceCls = model.Classes.FirstOrDefault(c => c.CSharpFullName == ifaceFqn);
            if (ifaceCls is null || SkipClass(ifaceCls)) continue;
            if (!nameByCSharp.TryGetValue(ifaceFqn, out var ifaceName)) continue;
            ifaceSchemas.Add(ifaceName + zs.SchemaConstSuffix);
        }

        sb.Append($"export const {schemaConst}{typeAnnotation} = {initializerPrefix}");
        if (baseSchema is not null)
        {
            // Base first, then interface composition, then own shape via .extend.
            sb.Append(baseSchema);
            foreach (var iface in ifaceSchemas)
                sb.Append($".merge({iface})");
            sb.AppendLine($".extend({{");
        }
        else if (ifaceSchemas.Count > 0)
        {
            // No base — lead with the first interface, merge the rest, add own
            // via .extend (matches the pattern: always end with .extend).
            sb.Append(ifaceSchemas[0]);
            for (int i = 1; i < ifaceSchemas.Count; i++)
                sb.Append($".merge({ifaceSchemas[i]})");
            sb.AppendLine($".extend({{");
        }
        else
        {
            // Plain class — straight z.object.
            sb.AppendLine($"z.object({{");
        }

        // Body: property lines, including the polymorphic discriminator literal
        // if this class is a variant.
        if (cls.PolymorphicDiscriminatorValue is not null
            && cls.PolymorphicDiscriminatorPropertyOnVariant is { } discName)
        {
            sb.AppendLine($"    {discName}: z.literal('{cls.PolymorphicDiscriminatorValue}'),");
        }

        // Base/interface fields are already covered by the composed schemas
        // (via base + interfaces) — skip anything declared on them to avoid
        // redeclaration (Zod .extend replaces keys but it's cleaner to dedupe).
        var coveredMemberNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var iface in cls.ImplementedInterfaces)
        {
            var ifaceCls = model.Classes.FirstOrDefault(c => c.CSharpFullName == iface);
            if (ifaceCls is null || SkipClass(ifaceCls)) continue;
            foreach (var p in ifaceCls.Properties)
                coveredMemberNames.Add(p.SourceName);
        }

        foreach (var prop in cls.Properties)
        {
            if (prop.TsIgnore) continue;
            if (coveredMemberNames.Contains(prop.SourceName)) continue;
            // Explicit TsName override bypasses the style transform — user said what they
            // wanted verbatim. Otherwise run the source name through the configured style.
            var name = prop.TsNameOverride ?? ApplyNameStyle(prop.SourceName, zs.PropertyNameStyle);
            var expr = BuildPropertyZodExpr(prop, nameByCSharp, cls.TypeParameters, zs.SchemaConstSuffix, lazySchemaNames, conform);
            sb.AppendLine($"    {name}: {expr},");
        }

        // [JsonExtensionData] → catchall on the schema. z.object({…}).catchall(V)
        // allows any unknown key with the given value schema — matches the
        // additionalProperties OpenAPI semantic.
        var catchall = "";
        if (cls.AllowsAdditionalProperties)
        {
            var catchallInner = cls.AdditionalPropertiesValueCSharpType is not null
                ? MapCSharpToZod(cls.AdditionalPropertiesValueCSharpType, isNullable: false, nameByCSharp, zs.SchemaConstSuffix, cls.TypeParameters)
                : "z.unknown()";
            catchall = $".catchall({catchallInner})";
        }
        sb.AppendLine($"}}){catchall}{initializerSuffix};");

        if (zs.EmitInferredTypes && !conform)
        {
            sb.AppendLine($"export type {cls.EmittedName} = z.infer<typeof {schemaConst}>;");
        }
        if (zs.EmitValidationGuards)
            sb.AppendLine($"export const {ValidationGuardName(cls.EmittedName)} = (value: unknown): value is z.output<typeof {schemaConst}> => {schemaConst}.validate(value);");
        sb.AppendLine();
    }

    private static void EmitEnum(StringBuilder sb, SchemaEnum en, ZodSettings zs)
    {
        if (SkipEnum(en)) return;
        var schemaConst = en.EmittedName + zs.SchemaConstSuffix;
        var conform = zs.ConformToTypeScriptTypes && (en.Targets & TypeTarget.TypeScript) != 0;
        var prefix = (zs.Compilation == ZodCompilationMode.Compile ? "z.compile(" : "")
            + (conform ? $"z.toZod<{en.EmittedName}>()(" : "");
        var suffix = new string(')', (zs.Compilation == ZodCompilationMode.Compile ? 1 : 0)
            + (conform ? 1 : 0));

        if (en.IsStringSerialized)
        {
            // z.enum(['A','B','C']) — exhaustive string literal union.
            var members = string.Join(", ", en.Members.Select(m => $"'{m.Name}'"));
            sb.AppendLine($"export const {schemaConst} = {prefix}z.enum([{members}]){suffix};");
        }
        else
        {
            // Numeric — z.union of z.literal(n) per member. z.nativeEnum would
            // require a TS enum at runtime, which we can't rely on (consumer may
            // take Zod target alone). Literal union works without a native enum
            // import and Zod narrows exhaustively.
            var literals = string.Join(", ", en.Members.Select(m => $"z.literal({m.Value})"));
            sb.AppendLine($"export const {schemaConst} = {prefix}z.union([{literals}]){suffix};");
        }

        if (zs.EmitInferredTypes && !conform)
        {
            sb.AppendLine($"export type {en.EmittedName} = z.infer<typeof {schemaConst}>;");
        }
        if (zs.EmitValidationGuards)
            sb.AppendLine($"export const {ValidationGuardName(en.EmittedName)} = (value: unknown): value is z.output<typeof {schemaConst}> => {schemaConst}.validate(value);");
        sb.AppendLine();
    }

    // ── property expression construction ────────────────────────────────────

    private static string BuildPropertyZodExpr(
        SchemaProperty prop,
        IReadOnlyDictionary<string, string> nameByCSharp,
        IReadOnlyList<string> typeParameters,
        string schemaConstSuffix,
        HashSet<string>? lazySchemaNames = null,
        bool conformToTypeScript = false)
    {
        var targetFqn = prop.TargetTypeCSharpFqn ?? prop.CSharpTypeFullName;
        var core = MapCSharpToZod(targetFqn, prop.IsNullable, nameByCSharp, schemaConstSuffix, typeParameters, lazySchemaNames);

        // Apply string-shaped constraints (length, regex, email/url/uuid formats).
        // Numeric constraints use gte/lte.
        core = ApplyStringConstraints(core, prop);
        core = ApplyNumericConstraints(core, prop);

        // Nullable + optional → .nullish() by default. In TypeScript conformance
        // mode, mirror the TS emitter's optional-only contract so z.toZod<T>()
        // can prove the schemas match. Read-only (computed) props stay optional —
        // server always produces a value, client doesn't supply one. Explicit
        // `[Required]` / `[ZRequired]` / C# `required` override NRT: field must
        // be provided, so no nullish/optional even if the C# type is `string?`.
        var effectivelyNullable = prop.IsNullable && !prop.IsExplicitlyRequired;
        if (effectivelyNullable)
            core += conformToTypeScript ? ".optional()" : ".nullish()";
        else if (prop.IsReadOnly || prop.IsPatchField || ExtractGeneric(targetFqn.TrimEnd('?'), "PatchField") is not null)
            core += ".optional()";

        return core;
    }

    private static string ApplyStringConstraints(string expr, SchemaProperty prop)
    {
        // Only apply length/regex/format when the property is string-shaped.
        // Cheap heuristic: the core Zod expression starts with z.string or
        // contains .string() — sidesteps threading the C# type through.
        var isString = expr.StartsWith("z.string", System.StringComparison.Ordinal);
        if (!isString) return expr;

        // Format markers come from validation attrs via OpenApiFormat — the
        // SchemaParser normalises [EmailAddress]/[ZEmail] → "email" etc. Zod 4
        // moved these to top-level factories (z.email(), z.uuid(), z.iso.datetime())
        // and deprecated the chained z.string().email() forms.
        expr = ApplyStringFormat(expr, prop.ZodFormat, prop.ZodFormatLength, prop.OpenApiFormat);

        if (prop.MinLength is int min) expr += $".min({min})";
        if (prop.MaxLength is int max) expr += $".max({max})";
        if (prop.Pattern is { } pat)
        {
            // Escape the pattern for a JS regex literal. Zod takes any RegExp,
            // so `.regex(/pat/)` is the idiomatic form.
            expr += $".regex(/{EscapeRegexForJs(pat)}/)";
        }
        return expr;
    }

    /// <summary>
    /// Applies a string <c>format</c> validator to a <c>z.string()</c>-based
    /// expression using the Zod 4 top-level factories (<c>z.email()</c>,
    /// <c>z.uuid()</c>, <c>z.url()</c>, <c>z.iso.datetime()</c>, <c>z.iso.date()</c>).
    /// The factory replaces the leading <c>z.string()</c> so any subsequent
    /// <c>.min()</c>/<c>.max()</c>/<c>.regex()</c> chain onto it.
    /// </summary>
    private static string ApplyStringFormat(string expr, ZodStringFormat? zodFormat, int? formatLength, string? format)
    {
        var factory = zodFormat switch
        {
            ZodStringFormat.Email => "z.email()",
            ZodStringFormat.Url => "z.url()",
            ZodStringFormat.Uuid => "z.uuid()",
            ZodStringFormat.Date => "z.iso.date()",
            ZodStringFormat.DateTime => "z.iso.datetime()",
            ZodStringFormat.Hostname => "z.hostname()",
            ZodStringFormat.Ulid => "z.ulid()",
            ZodStringFormat.NanoId when formatLength is > 0 => $"z.nanoid({{ length: {formatLength.Value} }})",
            ZodStringFormat.NanoId => "z.nanoid()",
            ZodStringFormat.Base64 => "z.base64()",
            ZodStringFormat.Base64Url => "z.base64url()",
            ZodStringFormat.CreditCard => "z.creditCard()",
            ZodStringFormat.Iban => "z.iban()",
            _ => format switch
            {
                "email" => "z.email()",
                "uri" or "url" => "z.url()",
                "uuid" => "z.uuid()",
                "date-time" => "z.iso.datetime()",
                "date" => "z.iso.date()",
                _ => null,
            },
        };
        if (factory is null) return expr; // no recognised format

        // Swap the leading `z.string()` base for the top-level factory.
        const string stringBase = "z.string()";
        return expr.StartsWith(stringBase, System.StringComparison.Ordinal)
            ? factory + expr.Substring(stringBase.Length)
            : expr;
    }

    private static string ApplyNumericConstraints(string expr, SchemaProperty prop)
    {
        var isNumber = expr.StartsWith("z.number", System.StringComparison.Ordinal);
        if (!isNumber) return expr;
        if (prop.Minimum is double lo) expr += $".gte({FormatNumber(lo)})";
        if (prop.Maximum is double hi) expr += $".lte({FormatNumber(hi)})";
        return expr;
    }

    private static string FormatNumber(double n) =>
        n == (long)n ? ((long)n).ToString(System.Globalization.CultureInfo.InvariantCulture)
                     : n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string EscapeRegexForJs(string pattern)
    {
        // Escape forward slash (literal / closes the JS regex) and backslashes
        // preserved verbatim otherwise — users expect their C# regex to port
        // 1:1 to JS. Character classes etc. are already valid JS regex syntax.
        return pattern.Replace("/", @"\/");
    }

    // ── type mapping ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a C# type expression to its Zod schema counterpart. Fallback for
    /// unknown types is <c>z.unknown()</c> — matches the TS emitter's <c>unknown</c>
    /// fallback and gets a <c>TG0002</c> diagnostic at the property source.
    /// </summary>
    private static string MapCSharpToZod(
        string cSharpType,
        bool isNullable,
        IReadOnlyDictionary<string, string> nameByCSharp,
        string schemaConstSuffix,
        IReadOnlyList<string>? typeParameters = null,
        HashSet<string>? lazySchemaNames = null)
    {
        var t = cSharpType.TrimEnd('?');

        // Type parameters on a generic class surface as `z.unknown()` in the
        // schema — Zod doesn't have first-class generics, and runtime-time the
        // value is whatever the caller validated separately. Keeps emission
        // unblocked for generic bases; refine later if real use cases demand
        // higher-order schema functions.
        if (typeParameters is not null && typeParameters.Contains(t)) return "z.unknown()";

        // Unwrap Dto's PatchField<T> tri-state — Zod consumers validate plain T.
        var patchInner = ExtractGeneric(t, "PatchField");
        if (patchInner != null) return MapCSharpToZod(patchInner, isNullable, nameByCSharp, schemaConstSuffix, typeParameters, lazySchemaNames);

        // User DTO reference → direct reference to the sibling schema const.
        // FilePerClass mode: import resolution runs before the z.object body
        // is evaluated, so direct refs are safe. SingleFile mode: the topo sort
        // guarantees declaration order.
        if (nameByCSharp.TryGetValue(t, out var mapped))
            return lazySchemaNames is not null && lazySchemaNames.Contains(mapped)
                ? $"z.lazy(() => {mapped}{schemaConstSuffix})"
                : mapped + schemaConstSuffix;

        if (t.EndsWith("[]"))
            return $"z.array({MapCSharpToZod(t.Substring(0, t.Length - 2), false, nameByCSharp, schemaConstSuffix, typeParameters, lazySchemaNames)})";
        var listMatch = ExtractGeneric(t, "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection");
        if (listMatch != null)
            return $"z.array({MapCSharpToZod(listMatch, false, nameByCSharp, schemaConstSuffix, typeParameters, lazySchemaNames)})";

        var dictMatch = ExtractTwoGenericArgs(t, "Dictionary", "IDictionary", "IReadOnlyDictionary");
        if (dictMatch != null)
            return $"z.record({MapCSharpToZod(dictMatch.Value.K, false, nameByCSharp, schemaConstSuffix, typeParameters, lazySchemaNames)}, {MapCSharpToZod(dictMatch.Value.V, false, nameByCSharp, schemaConstSuffix, typeParameters, lazySchemaNames)})";

        // Zod 4 top-level format factories — the chained z.string().uuid() forms
        // are deprecated in Zod 4. See also ApplyStringFormat for attr-driven formats.
        return t switch
        {
            "string" => "z.string()",
            "bool" or "System.Boolean" => "z.boolean()",
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                or "System.Int32" or "System.Int64" => "z.number().int()",
            "float" or "double" or "System.Single" or "System.Double" => "z.number()",
            "decimal" or "System.Decimal" => "z.string()",
            "System.Guid" or "Guid" => "z.uuid()",
            "System.DateTime" or "DateTime" or "System.DateTimeOffset" or "DateTimeOffset" => "z.iso.datetime()",
            "System.DateOnly" or "DateOnly" => "z.iso.date()",
            "System.TimeOnly" or "TimeOnly" or "System.TimeSpan" or "TimeSpan" => "z.string()",
            "object" => "z.unknown()",
            _ => "z.unknown()",
        };
    }

    // ── reference collection for per-file imports ───────────────────────────

    private static HashSet<string> CollectClassReferences(SchemaClass cls, IReadOnlyDictionary<string, string> nameByCSharp)
    {
        var acc = new HashSet<string>();
        if (cls.BaseClassFullName is { } bfn && nameByCSharp.TryGetValue(bfn, out var bn))
            acc.Add(bn);
        foreach (var arg in cls.BaseClassTypeArguments) CollectRefs(arg, nameByCSharp, acc);
        for (int i = 0; i < cls.ImplementedInterfaces.Count; i++)
        {
            if (nameByCSharp.TryGetValue(cls.ImplementedInterfaces[i], out var ifaceName)) acc.Add(ifaceName);
            foreach (var arg in cls.ImplementedInterfaceTypeArguments[i])
                CollectRefs(arg, nameByCSharp, acc);
        }
        // Polymorphic variants: union needs imports for every variant schema.
        foreach (var variant in cls.PolymorphicVariants)
            if (nameByCSharp.TryGetValue(variant.CSharpFullName, out var vn)) acc.Add(vn);

        foreach (var prop in cls.Properties)
        {
            if (prop.TsIgnore) continue;
            CollectRefs(prop.TargetTypeCSharpFqn ?? prop.CSharpTypeFullName, nameByCSharp, acc);
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

    // ── topological sort (SingleFile mode) ──────────────────────────────────

    private static HashSet<string> CollectLazySchemaNames(
        SchemaClass owner,
        SchemaModel model,
        IReadOnlyDictionary<string, string> nameByCSharp)
    {
        var classes = model.Classes.ToDictionary(c => c.CSharpFullName, c => c);
        var result = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var prop in owner.Properties)
        {
            foreach (var referenced in EnumerateReferencedTypes(prop.TargetTypeCSharpFqn ?? prop.CSharpTypeFullName, classes))
            {
                if (CanReach(referenced, owner.CSharpFullName, classes, new HashSet<string>(System.StringComparer.Ordinal))
                    && nameByCSharp.TryGetValue(referenced, out var emittedName))
                    result.Add(emittedName);
            }
        }
        return result;
    }

    private static bool CanReach(
        string current,
        string target,
        IReadOnlyDictionary<string, SchemaClass> classes,
        HashSet<string> visited)
    {
        if (current == target) return true;
        if (!visited.Add(current) || !classes.TryGetValue(current, out var cls)) return false;
        foreach (var prop in cls.Properties)
            foreach (var next in EnumerateReferencedTypes(prop.TargetTypeCSharpFqn ?? prop.CSharpTypeFullName, classes))
                if (CanReach(next, target, classes, visited)) return true;
        return false;
    }

    private static IEnumerable<string> EnumerateReferencedTypes(
        string cSharpType,
        IReadOnlyDictionary<string, SchemaClass> classes)
    {
        var t = cSharpType.TrimEnd('?');
        if (classes.ContainsKey(t)) { yield return t; yield break; }
        if (t.EndsWith("[]", System.StringComparison.Ordinal))
        {
            foreach (var item in EnumerateReferencedTypes(t.Substring(0, t.Length - 2), classes)) yield return item;
            yield break;
        }
        var single = ExtractGeneric(t, "PatchField", "Nullable", "List", "IList", "ICollection", "IEnumerable",
            "IReadOnlyList", "IReadOnlyCollection", "HashSet", "ISet", "IReadOnlySet");
        if (single is not null)
        {
            foreach (var item in EnumerateReferencedTypes(single, classes)) yield return item;
            yield break;
        }
        var pair = ExtractTwoGenericArgs(t, "Dictionary", "IDictionary", "IReadOnlyDictionary");
        if (pair is not null)
        {
            foreach (var item in EnumerateReferencedTypes(pair.Value.K, classes)) yield return item;
            foreach (var item in EnumerateReferencedTypes(pair.Value.V, classes)) yield return item;
        }
    }

    /// <summary>
    /// Returns classes ordered so every schema is declared before any that
    /// references it. Single-file mode can't forward-reference const bindings.
    /// Cycles (if any) fall back to source order — <c>z.lazy(…)</c> in the
    /// property expression breaks the cycle at runtime.
    /// </summary>
    private static List<SchemaClass> TopoSortClasses(SchemaModel model)
    {
        var byName = model.Classes.ToDictionary(c => c.CSharpFullName, c => c);
        var emitted = model.Classes.Where(c => !SkipClass(c)).ToList();
        var emittedSet = new HashSet<string>(emitted.Select(c => c.CSharpFullName));
        var visited = new HashSet<string>();
        var result = new List<SchemaClass>(emitted.Count);

        void Visit(SchemaClass c)
        {
            if (!visited.Add(c.CSharpFullName)) return;

            // Base class must come first (.extend() references it)
            if (c.BaseClassFullName is { } b && byName.TryGetValue(b, out var baseCls) && emittedSet.Contains(b))
                Visit(baseCls);
            foreach (var iface in c.ImplementedInterfaces)
                if (byName.TryGetValue(iface, out var iCls) && emittedSet.Contains(iface))
                    Visit(iCls);
            foreach (var variant in c.PolymorphicVariants)
                if (byName.TryGetValue(variant.CSharpFullName, out var vCls) && emittedSet.Contains(variant.CSharpFullName))
                    Visit(vCls);

            // Property type dependencies — a schema referencing another schema
            // (e.g. items: z.array(OrderItemSchema)) must come after the referenced one.
            foreach (var prop in c.Properties)
            {
                var propType = (prop.TargetTypeCSharpFqn ?? prop.CSharpTypeFullName).TrimEnd('?');
                // Unwrap collections: List<X>, X[], IEnumerable<X> etc.
                var inner = ExtractGeneric(propType, "List", "IList", "ICollection", "IEnumerable",
                    "IReadOnlyList", "IReadOnlyCollection", "HashSet");
                var resolved = inner ?? (propType.EndsWith("[]") ? propType.Substring(0, propType.Length - 2) : propType);
                if (byName.TryGetValue(resolved, out var depCls) && emittedSet.Contains(resolved))
                    Visit(depCls);
            }

            result.Add(c);
        }

        foreach (var c in emitted) Visit(c);
        return result;
    }

    // ── shared helpers (mirror of TypeScriptEmitter internals) ──────────────

    private static string? ExtractGeneric(string typeName, params string[] names)
    {
        foreach (var n in names)
        {
            var idx = typeName.IndexOf(n + "<", System.StringComparison.Ordinal);
            if (idx < 0) continue;
            if (idx > 0 && char.IsLetterOrDigit(typeName[idx - 1])) continue;
            var open = idx + n.Length;
            var inner = ExtractBalanced(typeName, open);
            if (inner != null) return inner;
        }
        return null;
    }

    private static string StripTsExtension(string fileName) =>
        fileName.EndsWith(".ts", System.StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - 3)
            : fileName;

    private static (string K, string V)? ExtractTwoGenericArgs(string typeName, params string[] names)
    {
        var inner = ExtractGeneric(typeName, names);
        if (inner is null) return null;
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

    private static string ValidationGuardName(string emittedName) =>
        string.IsNullOrEmpty(emittedName)
            ? "isValue"
            : "is" + char.ToUpperInvariant(emittedName[0]) + emittedName.Substring(1);

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

    private static string ResolveTypeName(string source, string? overrideName) =>
        overrideName ?? source;

    private static string ResolveOutputDir(string? globalDir, SchemaModel model)
    {
        if (!string.IsNullOrEmpty(globalDir)) return globalDir!;
        var first = model.Classes.FirstOrDefault();
        return first?.OutputDir ?? ".";
    }
}
