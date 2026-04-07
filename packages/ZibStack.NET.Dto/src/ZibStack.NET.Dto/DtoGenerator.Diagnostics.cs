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
    }

    private static void RunDiagnostics(SourceProductionContext spc, INamedTypeSymbol symbol, TypeDeclarationSyntax syntax)
    {
        var attrs = symbol.GetAttributes();
        var hasCreateDto = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
        var hasUpdateDto = attrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
        var hasCombined = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
        var hasPartialFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Utils.PartialFromAttribute");
        var hasIntersectFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Utils.IntersectFromAttribute");
        var hasCreateDtoFor = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoForAttributeFqn);
        var hasUpdateDtoFor = attrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoForAttributeFqn);
        var hasPickFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Utils.PickFromAttribute");
        var hasOmitFrom = attrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Utils.OmitFromAttribute");

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

            // SDTO004: CreateOnly without CreateDto
            if (!hasCreateDto && !hasCombined &&
                propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOnlyAttributeFqn))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.CreateOnlyWithoutCreateDto,
                    prop.Locations.FirstOrDefault() ?? syntax.Identifier.GetLocation(),
                    prop.Name, symbol.Name));
            }

            // SDTO005: UpdateOnly without UpdateDto
            if (!hasUpdateDto && !hasCombined &&
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

        // SDTO009-011: CrudApi diagnostics
        var hasCrudApi = attrs.Any(a => a.AttributeClass?.ToDisplayString() == CrudApiAttributeFqn);
        if (hasCrudApi)
        {
            if (!hasCreateDto && !hasUpdateDto && !hasCombined)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.CrudApiMissingWriteDto,
                    syntax.Identifier.GetLocation(), symbol.Name));
            }

            var hasResponseDto2 = attrs.Any(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
            if (!hasResponseDto2)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.CrudApiMissingResponseDto,
                    syntax.Identifier.GetLocation(), symbol.Name));
            }

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
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Utils.IntersectFromAttribute" ||
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Utils.PartialFromAttribute");

            if (!props.Any() && !hasExtraProps)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoProperties,
                    syntax.Identifier.GetLocation(), symbol.Name));
            }
        }
    }
}
