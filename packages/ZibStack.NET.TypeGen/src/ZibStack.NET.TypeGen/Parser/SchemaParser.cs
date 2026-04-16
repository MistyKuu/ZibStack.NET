using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZibStack.NET.TypeGen.Generator;

/// <summary>
/// Reads <c>[GenerateTypes]</c>-annotated symbols and produces a
/// <see cref="SchemaClass"/> / <see cref="SchemaEnum"/>. Per-class / per-property
/// override attributes are folded into the model here so emitters can stay
/// language-focused (don't need to re-walk attribute data).
/// </summary>
internal static class SchemaParser
{
    private const string GenerateTypesAttr = "ZibStack.NET.TypeGen.GenerateTypesAttribute";
    private const string TsNameAttr = "ZibStack.NET.TypeGen.TsNameAttribute";
    private const string TsTypeAttr = "ZibStack.NET.TypeGen.TsTypeAttribute";
    // Generic variant `[TsType<T>]`. Constructed form is `...TsTypeAttribute<T>` —
    // we match on the open-generic's name after a GetGenericTypeDefinition-style check.
    private const string TsTypeGenericAttrOpen = "ZibStack.NET.TypeGen.TsTypeAttribute<T>";
    private const string TsIgnoreAttr = "ZibStack.NET.TypeGen.TsIgnoreAttribute";
    private const string OpenApiSchemaNameAttr = "ZibStack.NET.TypeGen.OpenApiSchemaNameAttribute";
    private const string OpenApiPropertyAttr = "ZibStack.NET.TypeGen.OpenApiPropertyAttribute";
    private const string OpenApiIgnoreAttr = "ZibStack.NET.TypeGen.OpenApiIgnoreAttribute";
    // String-only — no reference to ZibStack.NET.Dto. The attribute is generated
    // by Dto's source generator into the user's compilation, so we read it via
    // its full metadata name without taking a binary dependency.
    private const string CrudApiAttr = "ZibStack.NET.Dto.CrudApiAttribute";

    /// <summary>
    /// Returns true if the symbol carries <c>[GenerateTypes]</c>. Cheap predicate
    /// for the incremental-generator filter step.
    /// </summary>
    public static bool HasGenerateTypes(INamedTypeSymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == GenerateTypesAttr);

    public static bool HasCrudApi(INamedTypeSymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == CrudApiAttr);

    public static SchemaClass? ParseClass(INamedTypeSymbol symbol) => ParseClassCore(symbol, null, null);

    /// <summary>
    /// Parses a class without requiring <c>[GenerateTypes]</c> and with explicit
    /// target/outputDir — used for auxiliary DTOs discovered by convention (e.g.
    /// <c>Create{Class}Request</c> produced by the Dto generator, which TypeGen
    /// auto-discovers so the <c>$ref</c>s from <c>[CrudApi]</c> paths resolve).
    /// </summary>
    public static SchemaClass? ParseAuxiliaryClass(INamedTypeSymbol symbol, TypeTarget target, string outputDir) =>
        ParseClassCore(symbol, target, outputDir);

    /// <summary>
    /// Walks the property graph of every class already in <paramref name="model"/>,
    /// pulling in any user-defined type (class, record, struct, enum) referenced by
    /// a property but not yet emitted. Discovered types inherit the root class's
    /// <c>Targets</c> and <c>OutputDir</c>, guaranteeing generated TS / OpenAPI
    /// references resolve (no stray <c>unknown</c> / missing <c>$ref</c>). External
    /// types — BCL (<c>System.*</c>, <c>Microsoft.*</c>, <c>Newtonsoft.*</c>) and
    /// any type outside the current compilation's assembly — are left alone; the
    /// user opts those in explicitly via <c>[TsType(..., ImportFrom = "...")]</c>.
    /// Cycles and diamond references are handled by an FQN-keyed "seen" set.
    /// </summary>
    public static void DiscoverTransitive(SchemaModel model, Compilation compilation)
    {
        // Symbol-equality-based tracking so `Node` (in the seed) and `Node?` (via a
        // nullable property) hash to the same entry and the cycle terminates.
        // `SymbolEqualityComparer.Default` already disregards nullable annotations.
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var c in model.Classes)
        {
            var s = compilation.GetTypeByMetadataName(StripGlobal(c.CSharpFullName));
            if (s is not null) seen.Add(s);
        }
        foreach (var e in model.Enums)
        {
            var s = compilation.GetTypeByMetadataName(StripGlobal(e.CSharpFullName));
            if (s is not null) seen.Add(s);
        }

        // BFS over the current classes — every new class we discover gets its
        // own properties walked too, transitively.
        var queue = new Queue<SchemaClass>(model.Classes);
        while (queue.Count > 0)
        {
            var cls = queue.Dequeue();
            var clsSymbol = compilation.GetTypeByMetadataName(StripGlobal(cls.CSharpFullName));
            if (clsSymbol is null) continue;

            // Walk the full inheritance chain, not just declared members. `inlineInherited`
            // in ParseClassCore has already flattened base properties into cls.Properties
            // for non-emitted bases, but discovery uses the actual property type SYMBOLS
            // to unwrap nullables / collections — and `GetMembers()` on the derived
            // symbol returns declared-only. Without the chain walk an enum referenced
            // only through an inherited property (e.g. `D : B : C<T>` where C declares
            // abstract `Type` of an enum) falls through to `unknown` in TS.
            // Duplicates from overrides are filtered by the name-keyed HashSet below.
            var seenProps = new HashSet<string>(System.StringComparer.Ordinal);
            for (var cur = clsSymbol; cur is not null && cur.SpecialType != SpecialType.System_Object; cur = cur.BaseType)
            foreach (var prop in cur.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.IsStatic || prop.IsIndexer) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (!seenProps.Add(prop.Name)) continue;

                foreach (var nested in ExtractNestedUserTypes(prop.Type, compilation))
                {
                    // Normalize nullable reference annotation so `Node` and `Node?`
                    // share a cache slot — `Add` returns false the second time.
                    var key = nested.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                    if (!seen.Add(key)) continue;

                    if (nested.TypeKind == TypeKind.Enum)
                    {
                        var e = ParseAuxiliaryEnum(nested, cls.Targets, cls.OutputDir);
                        if (e is not null) model.Enums.Add(e);
                    }
                    else
                    {
                        var sub = ParseAuxiliaryClass(nested, cls.Targets, cls.OutputDir);
                        if (sub is not null)
                        {
                            model.Classes.Add(sub);
                            queue.Enqueue(sub);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Walks every property's <c>[TsType&lt;T&gt;]</c> generic target and, when
    /// <c>T</c> is user-defined but not yet in the model, adds it as an auxiliary
    /// class / enum inheriting the referencing class's <c>Targets</c> and
    /// <c>OutputDir</c>. Call before <see cref="DiscoverTransitive"/> so that
    /// transitively-reached types of newly-seeded targets get pulled in too.
    /// External targets (BCL, other assemblies) are left alone — the user owns
    /// their <c>.ts</c> / <c>.d.ts</c> definition.
    /// </summary>
    public static void SeedGenericTsTypeTargets(SchemaModel model, Compilation compilation)
    {
        // Snapshot the current classes — we add to model.Classes while iterating.
        var inModel = new HashSet<string>(
            System.Linq.Enumerable.Concat(
                model.Classes.Select(c => c.CSharpFullName),
                model.Enums.Select(e => e.CSharpFullName)),
            System.StringComparer.Ordinal);
        var toAdd = new List<(SchemaClass Owner, INamedTypeSymbol Target)>();

        foreach (var cls in model.Classes)
        {
            foreach (var prop in cls.Properties)
            {
                if (prop.TsTypeTargetCSharpFqn is null) continue;
                if (inModel.Contains(prop.TsTypeTargetCSharpFqn)) continue;
                var sym = compilation.GetTypeByMetadataName(StripGlobal(prop.TsTypeTargetCSharpFqn));
                if (sym is null) continue;
                // `[TsType<T>]` is an explicit request to emit T — relax the
                // same-assembly check that DiscoverTransitive uses. Still skip
                // BCL (System.*, Microsoft.*) to avoid pulling in half the framework.
                if (!IsEmittableTypeForExplicitReference(sym)) continue;
                toAdd.Add((cls, sym));
                inModel.Add(prop.TsTypeTargetCSharpFqn);
            }
        }
        foreach (var (owner, sym) in toAdd)
        {
            if (sym.TypeKind == TypeKind.Enum)
            {
                var e = ParseAuxiliaryEnum(sym, owner.Targets, owner.OutputDir);
                if (e is not null) model.Enums.Add(e);
            }
            else
            {
                var c = ParseAuxiliaryClass(sym, owner.Targets, owner.OutputDir);
                if (c is not null) model.Classes.Add(c);
            }
        }
    }

    /// <summary>
    /// After the model is finalised (roots + companions + transitive discovery +
    /// fluent merge), rewrites every property carrying
    /// <see cref="SchemaProperty.TsTypeTargetCSharpFqn"/> so its
    /// <see cref="SchemaProperty.TsTypeOverride"/> points at the target's emitted
    /// TS name and its <see cref="SchemaProperty.TsImportFrom"/> — when unset by
    /// the user — is computed as a relative path from the owning class's
    /// <c>OutputDir</c> to the target's. Targets outside the model are left
    /// alone (the name already falls back to the generic argument's simple name
    /// during parsing; no import is emitted without an explicit <c>ImportFrom</c>).
    /// </summary>
    public static void ResolveGenericTsTypeReferences(SchemaModel model)
    {
        foreach (var cls in model.Classes)
        {
            foreach (var prop in cls.Properties)
            {
                if (prop.TsTypeTargetCSharpFqn is null) continue;

                var fqn = prop.TsTypeTargetCSharpFqn;
                var targetClass = model.Classes.FirstOrDefault(c => c.CSharpFullName == fqn);
                var targetEnum = targetClass is null
                    ? model.Enums.FirstOrDefault(e => e.CSharpFullName == fqn)
                    : null;

                string? emittedName = null;
                string? targetDir = null;
                if (targetClass is not null)
                {
                    emittedName = targetClass.TsNameOverride ?? targetClass.EmittedName;
                    targetDir = targetClass.OutputDir;
                }
                else if (targetEnum is not null)
                {
                    emittedName = targetEnum.TsNameOverride ?? targetEnum.EmittedName;
                    targetDir = targetEnum.OutputDir;
                }

                if (emittedName is null) continue;   // external — keep the fallback.
                prop.TsTypeOverride = emittedName;
                if (prop.TsImportFrom is null)
                    prop.TsImportFrom = ComputeRelativeImport(cls.OutputDir, targetDir!, emittedName);
            }
        }
    }

    /// <summary>
    /// Forms a TypeScript module specifier pointing from <paramref name="fromDir"/>
    /// to <paramref name="fileName"/> living in <paramref name="toDir"/>. Both
    /// directories are treated as relative-style paths (e.g. <c>"."</c>,
    /// <c>"client/src/api"</c>). Same directory collapses to <c>./Name</c>;
    /// otherwise up-traversals (<c>..</c>) bridge back to the common ancestor
    /// before descending.
    /// </summary>
    internal static string ComputeRelativeImport(string fromDir, string toDir, string fileName)
    {
        static string[] Segments(string dir) => dir.Replace('\\', '/')
            .Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != ".").ToArray();

        var from = Segments(fromDir);
        var to = Segments(toDir);

        int common = 0;
        while (common < from.Length && common < to.Length && from[common] == to[common])
            common++;

        var up = from.Length - common;
        var downs = to.Skip(common);
        var parts = new List<string>();
        for (int i = 0; i < up; i++) parts.Add("..");
        parts.AddRange(downs);
        parts.Add(fileName);
        var joined = string.Join("/", parts);
        return joined.StartsWith("..", System.StringComparison.Ordinal) ? joined : "./" + joined;
    }

    /// <summary>
    /// Yields every user-defined <see cref="INamedTypeSymbol"/> reachable from
    /// <paramref name="type"/>, unwrapping nullable, array, and common collection
    /// wrappers along the way. A "user-defined" type is one declared in the
    /// compilation's own assembly with a non-BCL namespace.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> ExtractNestedUserTypes(ITypeSymbol type, Compilation compilation)
    {
        // Unwrap nullable value types: T? -> T.
        if (type is INamedTypeSymbol nts1
            && nts1.IsGenericType
            && nts1.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            foreach (var x in ExtractNestedUserTypes(nts1.TypeArguments[0], compilation)) yield return x;
            yield break;
        }

        // Unwrap array: T[] -> T.
        if (type is IArrayTypeSymbol arr)
        {
            foreach (var x in ExtractNestedUserTypes(arr.ElementType, compilation)) yield return x;
            yield break;
        }

        if (type is not INamedTypeSymbol named) yield break;

        // Unwrap common collection wrappers — walk their type arguments.
        if (IsCollectionType(named))
        {
            foreach (var arg in named.TypeArguments)
                foreach (var x in ExtractNestedUserTypes(arg, compilation))
                    yield return x;
            yield break;
        }

        if (IsUserDefinedType(named, compilation))
            yield return named;
    }

    private static bool IsCollectionType(INamedTypeSymbol t)
    {
        var ctor = t.ConstructedFrom.ToDisplayString();
        return ctor == "System.Collections.Generic.List<T>"
            || ctor == "System.Collections.Generic.IList<T>"
            || ctor == "System.Collections.Generic.ICollection<T>"
            || ctor == "System.Collections.Generic.IEnumerable<T>"
            || ctor == "System.Collections.Generic.IReadOnlyList<T>"
            || ctor == "System.Collections.Generic.IReadOnlyCollection<T>"
            || ctor == "System.Collections.Generic.HashSet<T>"
            || ctor == "System.Collections.Generic.ISet<T>"
            || ctor == "System.Collections.Generic.IReadOnlySet<T>"
            || ctor == "System.Collections.Generic.Dictionary<TKey, TValue>"
            || ctor == "System.Collections.Generic.IDictionary<TKey, TValue>"
            || ctor == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
    }

    /// <summary>
    /// Looser version of <see cref="IsUserDefinedType"/> for types the user has
    /// <em>explicitly</em> referenced via <c>[TsType&lt;T&gt;]</c>. Drops the
    /// same-assembly constraint — a type from a referenced NuGet or another
    /// project is fair game when the user asked for it by name. BCL namespaces
    /// still get filtered to avoid accidentally dragging in framework internals.
    /// </summary>
    private static bool IsEmittableTypeForExplicitReference(INamedTypeSymbol t)
    {
        if (t.SpecialType != SpecialType.None) return false;
        if (t.TypeKind != TypeKind.Class && t.TypeKind != TypeKind.Struct && t.TypeKind != TypeKind.Enum)
            return false;
        var ns = t.ContainingNamespace?.ToDisplayString() ?? "";
        if (ns == "System" || ns.StartsWith("System.", System.StringComparison.Ordinal)) return false;
        if (ns.StartsWith("Microsoft.", System.StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool IsUserDefinedType(INamedTypeSymbol t, Compilation compilation)
    {
        // Primitives and well-known BCL types (int, string, Guid, DateTime, decimal, etc.).
        if (t.SpecialType != SpecialType.None) return false;
        if (t.TypeKind != TypeKind.Class && t.TypeKind != TypeKind.Struct && t.TypeKind != TypeKind.Enum)
            return false;
        // Must live in the compilation's own assembly — external packages are opaque
        // unless the user opts them in with [TsType(..., ImportFrom = "...")].
        if (!SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly))
            return false;
        // Skip BCL namespaces even if somehow compiled in-assembly (polyfills, etc.).
        var ns = t.ContainingNamespace?.ToDisplayString() ?? "";
        if (ns == "System" || ns.StartsWith("System.", System.StringComparison.Ordinal)) return false;
        if (ns.StartsWith("Microsoft.", System.StringComparison.Ordinal)) return false;
        if (ns.StartsWith("Newtonsoft.", System.StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>
    /// True when the symbol carries a <c>[JsonConverter(typeof(X))]</c> whose
    /// <c>X</c> is one of the well-known string-serialising enum converters —
    /// System.Text.Json's non-generic + generic <c>JsonStringEnumConverter</c>,
    /// or Newtonsoft.Json's <c>StringEnumConverter</c>. Other converters
    /// (custom, third-party) don't flip the flag — we don't guess their shape.
    /// </summary>
    private static bool HasStringifyingJsonConverter(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (attrName != "System.Text.Json.Serialization.JsonConverterAttribute"
                && attrName != "Newtonsoft.Json.JsonConverterAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol converter) continue;
            var converterFqn = converter.ConstructedFrom.ToDisplayString();
            if (converterFqn == "System.Text.Json.Serialization.JsonStringEnumConverter"
                || converterFqn == "System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>"
                || converterFqn == "Newtonsoft.Json.Converters.StringEnumConverter")
                return true;
        }
        return false;
    }

    private static string StripGlobal(string fqn) =>
        fqn.StartsWith("global::", System.StringComparison.Ordinal) ? fqn.Substring("global::".Length) : fqn;

    private static SchemaClass? ParseClassCore(INamedTypeSymbol symbol, TypeTarget? forceTarget, string? forceOutputDir)
    {
        TypeTarget targets;
        string outputDir;
        if (forceTarget is not null)
        {
            targets = forceTarget.Value;
            outputDir = forceOutputDir ?? ".";
        }
        else
        {
            var generateAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == GenerateTypesAttr);
            if (generateAttr is null) return null;
            (targets, outputDir) = ReadGenerateTypesArgs(generateAttr);
        }

        var baseSymbol = symbol.BaseType;
        // Walk up the chain looking for the nearest ancestor that's independently
        // emitted (has its own [GenerateTypes]). If one exists, we express the
        // relationship against THAT ancestor via extends/allOf — regardless of
        // whether there are un-annotated intermediates between us and it. The
        // intermediates' properties still get inlined into this class below,
        // stopping the walk at the emitted ancestor so its members don't
        // duplicate across both schemas.
        INamedTypeSymbol? emittedAncestor = null;
        for (var b = baseSymbol; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
        {
            if (HasGenerateTypes(b)) { emittedAncestor = b; break; }
        }

        // Pick the base to render against:
        //   - nearest [GenerateTypes] ancestor if present (hoists over intermediates),
        //   - else the immediate base (flattened via inlineInherited below),
        //   - else null for `object` / `ValueType` terminations.
        string? baseFullName =
            emittedAncestor?.ToDisplayString()
            ?? (baseSymbol is not null
                && baseSymbol.SpecialType != SpecialType.System_Object
                && baseSymbol.SpecialType != SpecialType.System_ValueType
                ? baseSymbol.ToDisplayString()
                : null);

        var cls = new SchemaClass
        {
            CSharpFullName = symbol.ToDisplayString(),
            SourceName = symbol.Name,
            EmittedName = symbol.Name,   // global StripSuffixes applied later in pipeline
            Targets = targets,
            OutputDir = outputDir,
            TsNameOverride = ReadStringArg(symbol, TsNameAttr, "Name"),
            OpenApiNameOverride = ReadStringArg(symbol, OpenApiSchemaNameAttr, "Name"),
            TsIgnore = HasAttr(symbol, TsIgnoreAttr),
            OpenApiIgnore = HasAttr(symbol, OpenApiIgnoreAttr),
            Crud = ReadCrudApi(symbol),
            BaseClassFullName = baseFullName,
        };

        // Inline inherited properties from un-annotated ancestors only — those
        // classes don't get their own schema, so their members have to land on us
        // or they're lost. Stop the walk at the first [GenerateTypes] ancestor
        // (exclusive): its properties ARE emitted separately, and the extends/allOf
        // we wrote above covers them. This is always safe to run — when there are
        // no un-annotated ancestors the loop terminates before doing anything.
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var m in symbol.GetMembers().OfType<IPropertySymbol>())
            if (!m.IsStatic && !m.IsIndexer && m.DeclaredAccessibility == Accessibility.Public)
                seen.Add(m.Name);

        for (var b = baseSymbol; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
        {
            if (HasGenerateTypes(b)) break;   // emitted separately; extends/$ref covers it.
            foreach (var member in b.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic || member.IsIndexer) continue;
                if (member.DeclaredAccessibility != Accessibility.Public) continue;
                if (!seen.Add(member.Name)) continue;
                cls.Properties.Add(ParseProperty(member));
            }
        }

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            // Skip indexer-style properties — no clean translation to TS / OpenAPI.
            if (member.IsIndexer) continue;

            // [JsonExtensionData] — catch-all for unmapped JSON keys. Doesn't appear
            // as a regular property in the emitted schema; instead the schema gains
            // additionalProperties (OpenAPI) / index signature (TypeScript). Value type
            // is the dictionary's V argument when typed, else null = permissive.
            if (HasJsonExtensionDataAttr(member))
            {
                cls.AllowsAdditionalProperties = true;
                cls.AdditionalPropertiesValueCSharpType = ExtractDictionaryValueType(member.Type);
                continue;
            }

            cls.Properties.Add(ParseProperty(member));
        }

        return cls;
    }

    private static bool HasJsonExtensionDataAttr(IPropertySymbol prop) =>
        prop.GetAttributes().Any(a =>
        {
            var name = a.AttributeClass?.ToDisplayString();
            return name == "System.Text.Json.Serialization.JsonExtensionDataAttribute"
                || name == "Newtonsoft.Json.JsonExtensionDataAttribute";
        });

    /// <summary>
    /// For a <c>Dictionary&lt;string, V&gt;</c> / <c>IDictionary&lt;string, V&gt;</c> /
    /// <c>IReadOnlyDictionary&lt;string, V&gt;</c> property type, returns V's display
    /// string. Returns <c>null</c> for plain <c>object</c>, <c>JsonElement</c>, or
    /// anything not matching the dictionary shape — emitters fall back to permissive.
    /// </summary>
    private static string? ExtractDictionaryValueType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol nts || !nts.IsGenericType || nts.TypeArguments.Length != 2) return null;
        var def = nts.ConstructedFrom.ToDisplayString();
        if (def != "System.Collections.Generic.Dictionary<TKey, TValue>"
            && def != "System.Collections.Generic.IDictionary<TKey, TValue>"
            && def != "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            return null;
        var v = nts.TypeArguments[1].ToDisplayString();
        // Treat object / JsonElement as "no constraint" — emitters render permissive.
        if (v is "object" or "object?" or "System.Text.Json.JsonElement"
            or "System.Text.Json.JsonElement?" or "Newtonsoft.Json.Linq.JToken"
            or "Newtonsoft.Json.Linq.JToken?")
            return null;
        return v;
    }

    public static SchemaEnum? ParseEnum(INamedTypeSymbol symbol) => ParseEnumCore(symbol, null, null);

    /// <summary>
    /// Same as <see cref="ParseEnum"/> but forces <c>Targets</c> / <c>OutputDir</c>
    /// instead of reading them from <c>[GenerateTypes]</c>. Used by transitive
    /// discovery — the enum inherits its root's emission config.
    /// </summary>
    public static SchemaEnum? ParseAuxiliaryEnum(INamedTypeSymbol symbol, TypeTarget target, string outputDir) =>
        ParseEnumCore(symbol, target, outputDir);

    private static SchemaEnum? ParseEnumCore(INamedTypeSymbol symbol, TypeTarget? forceTarget, string? forceOutputDir)
    {
        if (symbol.TypeKind != TypeKind.Enum) return null;

        TypeTarget targets;
        string outputDir;
        if (forceTarget is not null)
        {
            targets = forceTarget.Value;
            outputDir = forceOutputDir ?? ".";
        }
        else
        {
            var generateAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == GenerateTypesAttr);
            if (generateAttr is null) return null;
            (targets, outputDir) = ReadGenerateTypesArgs(generateAttr);
        }

        var e = new SchemaEnum
        {
            CSharpFullName = symbol.ToDisplayString(),
            SourceName = symbol.Name,
            EmittedName = symbol.Name,
            Targets = targets,
            OutputDir = outputDir,
            TsNameOverride = ReadStringArg(symbol, TsNameAttr, "Name"),
            OpenApiNameOverride = ReadStringArg(symbol, OpenApiSchemaNameAttr, "Name"),
            TsIgnore = HasAttr(symbol, TsIgnoreAttr),
            OpenApiIgnore = HasAttr(symbol, OpenApiIgnoreAttr),
            IsStringSerialized = HasStringifyingJsonConverter(symbol),
        };

        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.HasConstantValue) continue;
            e.Members.Add(new SchemaEnumMember
            {
                Name = field.Name,
                Value = System.Convert.ToInt64(field.ConstantValue),
            });
        }

        return e;
    }

    private static SchemaProperty ParseProperty(IPropertySymbol prop)
    {
        var sp = new SchemaProperty
        {
            SourceName = prop.Name,
            CSharpTypeFullName = prop.Type.ToDisplayString(),
            // NRT-aware nullable. For value types `int?` Type.NullableAnnotation is also Annotated.
            IsNullable = prop.NullableAnnotation == NullableAnnotation.Annotated,
            Location = prop.Locations.FirstOrDefault(),
            TsNameOverride = ReadStringArg(prop, TsNameAttr, "Name"),
            TsTypeOverride = ReadStringArg(prop, TsTypeAttr, "TypeExpression"),
            TsImportFrom = ReadNamedStringArg(prop, TsTypeAttr, "ImportFrom"),
            OpenApiNameOverride = ReadStringArg(prop, OpenApiSchemaNameAttr, "Name"),
            TsIgnore = HasAttr(prop, TsIgnoreAttr),
            OpenApiIgnore = HasAttr(prop, OpenApiIgnoreAttr),
        };

        // [TsType<T>] generic form — captures T's FQN now; actual TS name +
        // import path get resolved late in the pipeline (see
        // ResolveGenericTsTypeReferences) after discovery has finalised the model.
        var genericTsType = prop.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is INamedTypeSymbol nts
            && nts.IsGenericType
            && nts.ConstructedFrom.ToDisplayString() == TsTypeGenericAttrOpen
            && nts.TypeArguments.Length == 1);
        if (genericTsType is not null
            && ((INamedTypeSymbol)genericTsType.AttributeClass!).TypeArguments[0] is ITypeSymbol tArg)
        {
            sp.TsTypeTargetCSharpFqn = tArg.ToDisplayString();
            // Seed TsTypeOverride with the target's simple name as a fallback —
            // the resolver will replace it with the target's EmittedName / [TsName]
            // override once the model is finalised.
            sp.TsTypeOverride ??= tArg.Name;
            // Optional explicit ImportFrom override — falls through if absent.
            foreach (var na in genericTsType.NamedArguments)
                if (na.Key == "ImportFrom" && na.Value.Value is string imp)
                    sp.TsImportFrom ??= imp;
        }

        var openApiAttr = prop.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == OpenApiPropertyAttr);
        if (openApiAttr is not null)
        {
            foreach (var named in openApiAttr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Format": sp.OpenApiFormat = named.Value.Value as string; break;
                    case "Example": sp.OpenApiExample = named.Value.Value; break;
                    case "Description": sp.OpenApiDescription = named.Value.Value as string; break;
                    case "Nullable": sp.OpenApiNullableOverride = named.Value.Value as bool?; break;
                }
            }
        }

        ReadValidationAttributes(prop, sp);
        return sp;
    }

    /// <summary>
    /// Translates DataAnnotations / ZibStack.Validation attributes into the
    /// corresponding OpenAPI schema constraints. Read by metadata name — no
    /// binary dependency on either package. Both families map to the same
    /// SchemaProperty fields so the emitter doesn't care which source produced
    /// the constraint.
    /// </summary>
    private static void ReadValidationAttributes(IPropertySymbol prop, SchemaProperty sp)
    {
        foreach (var a in prop.GetAttributes())
        {
            var name = a.AttributeClass?.ToDisplayString();
            if (name is null) continue;
            switch (name)
            {
                // ── System.ComponentModel.DataAnnotations ──
                case "System.ComponentModel.DataAnnotations.MinLengthAttribute":
                    if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is int min)
                        sp.MinLength = min;
                    break;
                case "System.ComponentModel.DataAnnotations.MaxLengthAttribute":
                    if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is int max)
                        sp.MaxLength = max;
                    break;
                case "System.ComponentModel.DataAnnotations.StringLengthAttribute":
                    if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is int sMax)
                        sp.MaxLength = sMax;
                    foreach (var na in a.NamedArguments)
                        if (na.Key == "MinimumLength" && na.Value.Value is int sMin) sp.MinLength = sMin;
                    break;
                case "System.ComponentModel.DataAnnotations.RangeAttribute":
                    ReadRangeCtorArgs(a, sp);
                    break;
                case "System.ComponentModel.DataAnnotations.RegularExpressionAttribute":
                    if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is string pat)
                        sp.Pattern = pat;
                    break;
                case "System.ComponentModel.DataAnnotations.EmailAddressAttribute":
                    sp.OpenApiFormat ??= "email";
                    break;
                case "System.ComponentModel.DataAnnotations.UrlAttribute":
                    sp.OpenApiFormat ??= "uri";
                    break;

                // ── ZibStack.NET.Validation (Z*) ──
                case "ZibStack.NET.Validation.ZMinLengthAttribute":
                    if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is int zMin)
                        sp.MinLength = zMin;
                    break;
                case "ZibStack.NET.Validation.ZMaxLengthAttribute":
                    if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is int zMax)
                        sp.MaxLength = zMax;
                    break;
                case "ZibStack.NET.Validation.ZRangeAttribute":
                    ReadRangeCtorArgs(a, sp);
                    break;
                case "ZibStack.NET.Validation.ZMatchAttribute":
                    if (a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is string zPat)
                        sp.Pattern = zPat;
                    break;
                case "ZibStack.NET.Validation.ZEmailAttribute":
                    sp.OpenApiFormat ??= "email";
                    break;
                case "ZibStack.NET.Validation.ZUrlAttribute":
                    sp.OpenApiFormat ??= "uri";
                    break;
                case "ZibStack.NET.Validation.ZNotEmptyAttribute":
                    // Approximation — for strings "non-empty" includes whitespace rules
                    // OpenAPI can't express, but minLength: 1 rules out empty strings / arrays.
                    sp.MinLength ??= 1;
                    break;
            }
        }
    }

    /// <summary>
    /// Both <c>System.ComponentModel.DataAnnotations.RangeAttribute</c> and
    /// ZibStack's <c>ZRangeAttribute</c> take (min, max) as positional args;
    /// values may be int, double, or long depending on overload. Normalize to double.
    /// </summary>
    private static void ReadRangeCtorArgs(AttributeData a, SchemaProperty sp)
    {
        if (a.ConstructorArguments.Length < 2) return;
        if (TryReadNumeric(a.ConstructorArguments[0].Value, out var min)) sp.Minimum = min;
        if (TryReadNumeric(a.ConstructorArguments[1].Value, out var max)) sp.Maximum = max;
    }

    private static bool TryReadNumeric(object? value, out double result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case long l: result = l; return true;
            case double d: result = d; return true;
            case float f: result = f; return true;
            case decimal m: result = (double)m; return true;
            default: result = 0; return false;
        }
    }

    private static (TypeTarget Targets, string OutputDir) ReadGenerateTypesArgs(AttributeData attr)
    {
        TypeTarget targets = TypeTarget.None;
        string outputDir = ".";
        // Constructor arg (optional): GenerateTypesAttribute(TypeTarget targets)
        if (attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is int t)
        {
            targets = (TypeTarget)t;
        }
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "Targets": if (named.Value.Value is int tv) targets = (TypeTarget)tv; break;
                case "OutputDir": if (named.Value.Value is string od) outputDir = od; break;
            }
        }
        return (targets, outputDir);
    }

    private static string? ReadStringArg(ISymbol symbol, string attrFullName, string namedKey)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attrFullName);
        if (attr is null) return null;
        // Try positional ctor arg first.
        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string ctorVal)
            return ctorVal;
        foreach (var na in attr.NamedArguments)
            if (na.Key == namedKey && na.Value.Value is string nv) return nv;
        return null;
    }

    /// <summary>
    /// Like <see cref="ReadStringArg"/> but skips the positional-ctor short-circuit —
    /// reads strictly from named arguments. Use when the attribute has both a positional
    /// ctor arg AND additional named props (e.g. <c>[TsType("Foo", ImportFrom = "...")]</c>).
    /// </summary>
    private static string? ReadNamedStringArg(ISymbol symbol, string attrFullName, string namedKey)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attrFullName);
        if (attr is null) return null;
        foreach (var na in attr.NamedArguments)
            if (na.Key == namedKey && na.Value.Value is string nv) return nv;
        return null;
    }

    private static CrudApiInfo? ReadCrudApi(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudApiAttr);
        if (attr is null) return null;
        var info = new CrudApiInfo();
        foreach (var na in attr.NamedArguments)
        {
            switch (na.Key)
            {
                case "Route": info.Route = na.Value.Value as string; break;
                case "RoutePrefix": info.RoutePrefix = na.Value.Value as string; break;
                case "KeyProperty": if (na.Value.Value is string kp && kp.Length > 0) info.KeyProperty = kp; break;
                case "Operations": if (na.Value.Value is int ops) info.Operations = (CrudOperations)ops; break;
            }
        }
        return info;
    }

    private static bool HasAttr(ISymbol symbol, string attrFullName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attrFullName);
}
