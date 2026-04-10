using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Dto;

public partial class DtoGenerator
{
    private static class Diagnostics
    {
        public static readonly DiagnosticDescriptor NoProperties = new(
            "SDTO001", "No properties to generate",
            "Type '{0}' has no public properties for DTO generation",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor MustBePartial = new(
            "SDTO002", "Type must be partial",
            "Type '{0}' must be declared as partial to use [{1}]",
            "ZibStack.NET.Dto", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor RenamePropertyNotFound = new(
            "SDTO003", "Property not found for rename",
            "Property '{0}' specified in [RenameProperty] does not exist on type '{1}'",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor CreateOnlyWithoutCreateDto = new(
            "SDTO004", "CreateOnly without CreateDto",
            "Property '{0}' has [CreateOnly] but type '{1}' has no [CreateDto] — attribute has no effect",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor UpdateOnlyWithoutUpdateDto = new(
            "SDTO005", "UpdateOnly without UpdateDto",
            "Property '{0}' has [UpdateOnly] but type '{1}' has no [UpdateDto] — attribute has no effect",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor FlattenOnPrimitive = new(
            "SDTO006", "Cannot flatten primitive type",
            "Property '{0}' has [Flatten] but its type '{1}' is not a complex type",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor RequiredPropertyIgnored = new(
            "SDTO007", "Required property ignored",
            "Property '{0}' is required but has [DtoIgnore] — ToEntity() may fail at runtime",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor DuplicateRenameTo = new(
            "SDTO008", "Duplicate rename target",
            "Multiple [RenameProperty] attributes target the same name '{0}'",
            "ZibStack.NET.Dto", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor CrudApiMissingResponseDto = new(
            "SDTO009", "CrudApi without ResponseDto",
            "Type '{0}' has [CrudApi] but no [ResponseDto] — GET endpoints will return raw entity",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor CrudApiMissingWriteDto = new(
            "SDTO010", "CrudApi without write DTOs",
            "Type '{0}' has [CrudApi] but no [CreateDto]/[UpdateDto]/[CreateOrUpdateDto] — write endpoints will not be generated",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor CrudApiKeyNotFound = new(
            "SDTO011", "CrudApi key property not found",
            "Type '{0}' has [CrudApi(KeyProperty = \"{1}\")] but no such property exists",
            "ZibStack.NET.Dto", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor CrudApiMissingStore = new(
            "SDTO012", "CrudApi type has no ICrudStore registered",
            "Type '{0}' has [CrudApi] but no DbSet<{0}> was found in any [GenerateCrudStores] DbContext. " +
            "Generated endpoints require 'ICrudStore<{0}, {1}>' from DI — add 'public DbSet<{0}> {0}s {{ get; set; }}' " +
            "to your DbContext, or register the store manually with 'services.AddScoped<ICrudStore<{0}, {1}>, YourStore>();'. " +
            "Without this, the first request that hits a generated endpoint throws a runtime body-inference error from ASP.NET routing.",
            "ZibStack.NET.Dto", DiagnosticSeverity.Warning, true);
    }

    /// <summary>Lightweight, equatable target for the SDTO012 diagnostic — pulled out of the regular pipeline so the diagnostic can be combined with the global DbSet collection.</summary>
    private sealed class CrudApiDiagTarget : System.IEquatable<CrudApiDiagTarget>
    {
        public string ClassName { get; }
        public string FullyQualifiedName { get; }
        public string KeyTypeName { get; }
        public DiagLocation? Location { get; }

        public CrudApiDiagTarget(string className, string fullyQualifiedName, string keyTypeName, DiagLocation? location)
        {
            ClassName = className;
            FullyQualifiedName = fullyQualifiedName;
            KeyTypeName = keyTypeName;
            Location = location;
        }

        public bool Equals(CrudApiDiagTarget? other) =>
            other is not null && FullyQualifiedName == other.FullyQualifiedName && KeyTypeName == other.KeyTypeName;
        public override bool Equals(object? obj) => Equals(obj as CrudApiDiagTarget);
        public override int GetHashCode() => unchecked(FullyQualifiedName.GetHashCode() * 397 ^ KeyTypeName.GetHashCode());
    }

    /// <summary>Equatable, value-based snapshot of a Roslyn Location so the incremental pipeline can cache it.</summary>
    private sealed class DiagLocation : System.IEquatable<DiagLocation>
    {
        public string FilePath { get; }
        public Microsoft.CodeAnalysis.Text.TextSpan TextSpan { get; }
        public Microsoft.CodeAnalysis.Text.LinePositionSpan LineSpan { get; }

        public DiagLocation(string filePath, Microsoft.CodeAnalysis.Text.TextSpan span, Microsoft.CodeAnalysis.Text.LinePositionSpan lineSpan)
        {
            FilePath = filePath;
            TextSpan = span;
            LineSpan = lineSpan;
        }

        public Location ToLocation() => Microsoft.CodeAnalysis.Location.Create(FilePath, TextSpan, LineSpan);

        public static DiagLocation? From(Location? loc)
        {
            if (loc is null || loc.SourceTree is null) return null;
            return new DiagLocation(loc.SourceTree.FilePath, loc.SourceSpan, loc.GetLineSpan().Span);
        }

        public bool Equals(DiagLocation? other) =>
            other is not null && FilePath == other.FilePath && TextSpan == other.TextSpan;
        public override bool Equals(object? obj) => Equals(obj as DiagLocation);
        public override int GetHashCode() => unchecked((FilePath?.GetHashCode() ?? 0) * 397 ^ TextSpan.GetHashCode());
    }

    /// <summary>Extract a CrudApiDiagTarget from a [CrudApi]-annotated type for the SDTO012 diagnostic pipeline.</summary>
    private static CrudApiDiagTarget? GetCrudApiDiagTarget(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol) return null;

        // Determine key type the same way the rest of the generator does:
        // [CrudApi(KeyProperty="X")] override, else "Id", else "{ClassName}Id".
        var crudAttr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudApiAttributeFqn);
        var keyName = crudAttr?.NamedArguments.FirstOrDefault(a => a.Key == "KeyProperty").Value.Value as string ?? "Id";
        var keyProp = GetAllProperties(symbol).FirstOrDefault(p => p.Name == keyName)
                    ?? GetAllProperties(symbol).FirstOrDefault(p => p.Name == "Id")
                    ?? GetAllProperties(symbol).FirstOrDefault(p => p.Name == symbol.Name + "Id");
        if (keyProp is null) return null; // SDTO011 covers this case

        return new CrudApiDiagTarget(
            symbol.Name,
            symbol.ToDisplayString(),
            keyProp.Type.ToDisplayString(),
            DiagLocation.From(symbol.Locations.FirstOrDefault()));
    }

    /// <summary>Pulls all entity types exposed via DbSet&lt;T&gt; in any DbContext-derived class. Used by the SDTO012 diagnostic to detect [CrudApi] types that aren't backed by a generated EF store.</summary>
    private static System.Collections.Immutable.ImmutableArray<string> GetDbSetEntityTypes(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax cds) return System.Collections.Immutable.ImmutableArray<string>.Empty;
        if (context.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) return System.Collections.Immutable.ImmutableArray<string>.Empty;

        // Walk base types looking for Microsoft.EntityFrameworkCore.DbContext
        bool isDbContext = false;
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            if (baseType.ToDisplayString() == "Microsoft.EntityFrameworkCore.DbContext") { isDbContext = true; break; }
            baseType = baseType.BaseType;
        }
        if (!isDbContext) return System.Collections.Immutable.ImmutableArray<string>.Empty;

        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.Type is not INamedTypeSymbol propType || !propType.IsGenericType) continue;
            if (propType.ConstructedFrom.ToDisplayString() != "Microsoft.EntityFrameworkCore.DbSet<TEntity>") continue;
            if (propType.TypeArguments[0] is INamedTypeSymbol entity)
                builder.Add(entity.ToDisplayString());
        }
        return builder.ToImmutable();
    }

    private static void RunDiagnostics(SourceProductionContext spc, INamedTypeSymbol symbol, TypeDeclarationSyntax syntax)
    {
        var attrs = symbol.GetAttributes();
        var hasCreateDto = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
        var hasUpdateDto = attrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
        var hasCombined = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
        var hasPartialFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.PartialFromAttribute");
        var hasIntersectFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.IntersectFromAttribute");
        var hasCreateDtoFor = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoForAttributeFqn);
        var hasUpdateDtoFor = attrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoForAttributeFqn);
        var hasPickFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.PickFromAttribute");
        var hasOmitFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.OmitFromAttribute");
        var hasCrudApi = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CrudApiAttributeFqn);

        var isPartial = syntax.Modifiers.Any(SyntaxKind.PartialKeyword);

        // SDTO002: Must be partial for DtoFor/PartialFrom/IntersectFrom/PickFrom/OmitFrom
        if (!isPartial)
        {
            if (hasPartialFrom)
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name, "PartialFrom"));
            if (hasIntersectFrom)
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name, "IntersectFrom"));
            if (hasCreateDtoFor)
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name, "CreateDtoFor"));
            if (hasUpdateDtoFor)
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name, "UpdateDtoFor"));
            if (hasPickFrom)
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name, "PickFrom"));
            if (hasOmitFrom)
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name, "OmitFrom"));
        }

        // SDTO003: RenameProperty — check property exists
        // SDTO008: Duplicate rename targets
        var renameTargets = new HashSet<string>();
        foreach (var a in attrs)
        {
            if (a.AttributeClass?.ToDisplayString() != RenamePropertyAttributeFqn) continue;
            if (a.ConstructorArguments.Length < 2) continue;

            var from = a.ConstructorArguments[0].Value as string;
            var to = a.ConstructorArguments[1].Value as string;

            if (from is not null)
            {
                var propExists = GetAllProperties(symbol).Any(p => p.Name == from);
                if (!propExists && !hasCreateDtoFor && !hasUpdateDtoFor)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.RenamePropertyNotFound,
                        syntax.Identifier.GetLocation(), from, symbol.Name));
                }
            }

            if (to is not null && !renameTargets.Add(to))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateRenameTo,
                    syntax.Identifier.GetLocation(), to));
            }
        }

        // Property-level diagnostics
        foreach (var prop in GetAllProperties(symbol))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;

            var propAttrs = prop.GetAttributes();

            // SDTO004: CreateOnly without CreateDto (skip if [CrudApi] auto-implies it)
            if (!hasCreateDto && !hasCombined && !hasCrudApi &&
                propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOnlyAttributeFqn))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.CreateOnlyWithoutCreateDto,
                    prop.Locations.FirstOrDefault() ?? syntax.Identifier.GetLocation(),
                    prop.Name, symbol.Name));
            }

            // SDTO005: UpdateOnly without UpdateDto (skip if [CrudApi] auto-implies it)
            if (!hasUpdateDto && !hasCombined && !hasCrudApi &&
                propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateOnlyAttributeFqn))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UpdateOnlyWithoutUpdateDto,
                    prop.Locations.FirstOrDefault() ?? syntax.Identifier.GetLocation(),
                    prop.Name, symbol.Name));
            }

            // SDTO006: Flatten on primitive
            if (propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == FlattenAttributeFqn))
            {
                var unwrapped = prop.Type;
                if (unwrapped is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nul)
                    unwrapped = nul.TypeArguments[0];

                if (unwrapped is not INamedTypeSymbol named || !IsComplexType(named))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.FlattenOnPrimitive,
                        prop.Locations.FirstOrDefault() ?? syntax.Identifier.GetLocation(),
                        prop.Name, prop.Type.ToDisplayString()));
                }
            }

            // SDTO007: Required property with DtoIgnore
            if (prop.IsRequired &&
                propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == DtoIgnoreAttributeFqn))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.RequiredPropertyIgnored,
                    prop.Locations.FirstOrDefault() ?? syntax.Identifier.GetLocation(),
                    prop.Name));
            }
        }

        // SDTO011: CrudApi key property not found
        if (hasCrudApi)
        {
            var crudAttr = attrs.First(a => a.AttributeClass?.ToDisplayString() == CrudApiAttributeFqn);
            var keyPropName = crudAttr.NamedArguments.FirstOrDefault(a => a.Key == "KeyProperty").Value.Value as string ?? "Id";
            var keyExists = GetAllProperties(symbol).Any(p => p.Name == keyPropName);
            if (!keyExists)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.CrudApiKeyNotFound,
                    syntax.Identifier.GetLocation(), symbol.Name, keyPropName));
            }
        }

        // SDTO001: No properties (only for CreateDto/UpdateDto/Combined — not DtoFor which reads from target)
        if (hasCreateDto || hasUpdateDto || hasCombined)
        {
            var props = GetAllProperties(symbol)
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.SetMethod is not null && p.GetMethod is not null)
                .Where(p => !p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == DtoIgnoreAttributeFqn));

            // Also count IntersectFrom/PartialFrom target properties
            var hasExtraProps = attrs.Any(a =>
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.IntersectFromAttribute" ||
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.PartialFromAttribute");

            if (!props.Any() && !hasExtraProps)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoProperties,
                    syntax.Identifier.GetLocation(), symbol.Name));
            }
        }
    }
}
