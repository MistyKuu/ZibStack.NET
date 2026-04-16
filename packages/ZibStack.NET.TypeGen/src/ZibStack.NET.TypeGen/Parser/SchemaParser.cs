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
        // Treat `object` / `ValueType` as "no base" — no inheritance to express.
        var baseFullName = baseSymbol is not null
                           && baseSymbol.SpecialType != SpecialType.System_Object
                           && baseSymbol.SpecialType != SpecialType.System_ValueType
            ? baseSymbol.ToDisplayString()
            : null;

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

        // If the base is itself [GenerateTypes]-annotated, emitters will express
        // the relationship via allOf / extends — keep `cls.Properties` to declared-only.
        // If not, inline inherited properties into this class so they aren't lost.
        var inlineInherited = baseSymbol is not null
            && baseFullName is not null
            && !HasGenerateTypes(baseSymbol);

        if (inlineInherited)
        {
            for (var b = baseSymbol; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
                foreach (var member in b.GetMembers().OfType<IPropertySymbol>())
                {
                    if (member.IsStatic || member.IsIndexer) continue;
                    if (member.DeclaredAccessibility != Accessibility.Public) continue;
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

    public static SchemaEnum? ParseEnum(INamedTypeSymbol symbol)
    {
        if (symbol.TypeKind != TypeKind.Enum) return null;
        var generateAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == GenerateTypesAttr);
        if (generateAttr is null) return null;

        var (targets, outputDir) = ReadGenerateTypesArgs(generateAttr);

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
            TsNameOverride = ReadStringArg(prop, TsNameAttr, "Name"),
            TsTypeOverride = ReadStringArg(prop, TsTypeAttr, "TypeExpression"),
            OpenApiNameOverride = ReadStringArg(prop, OpenApiSchemaNameAttr, "Name"),
            TsIgnore = HasAttr(prop, TsIgnoreAttr),
            OpenApiIgnore = HasAttr(prop, OpenApiIgnoreAttr),
        };

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
