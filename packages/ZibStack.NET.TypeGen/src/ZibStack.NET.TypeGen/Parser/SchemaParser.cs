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

    /// <summary>
    /// Returns true if the symbol carries <c>[GenerateTypes]</c>. Cheap predicate
    /// for the incremental-generator filter step.
    /// </summary>
    public static bool HasGenerateTypes(INamedTypeSymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == GenerateTypesAttr);

    public static SchemaClass? ParseClass(INamedTypeSymbol symbol)
    {
        var generateAttr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == GenerateTypesAttr);
        if (generateAttr is null) return null;

        var (targets, outputDir) = ReadGenerateTypesArgs(generateAttr);

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
        };

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            // Skip indexer-style properties — no clean translation to TS / OpenAPI.
            if (member.IsIndexer) continue;

            cls.Properties.Add(ParseProperty(member));
        }

        return cls;
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

        return sp;
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

    private static bool HasAttr(ISymbol symbol, string attrFullName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attrFullName);
}
