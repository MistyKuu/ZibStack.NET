using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Dto;

public partial class DtoGenerator
{
    // ─── Metadata extraction ───────────────────────────────────────────

    private static string SanitizeHintName(string name)
        => name.Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_').Replace('?', '_');

    private static DtoClassInfo? GetDtoInfo(GeneratorAttributeSyntaxContext context, DtoKind kind, string attributeFqn)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;

        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeFqn);
        if (attr is null) return null;

        var nameArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;

        string? createValidator = null;
        string? updateValidator = null;

        if (kind == DtoKind.Combined)
        {
            var cvArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "CreateValidator");
            createValidator = (cvArg.Value.Value as INamedTypeSymbol)
                ?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var uvArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "UpdateValidator");
            updateValidator = (uvArg.Value.Value as INamedTypeSymbol)
                ?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }
        else
        {
            var vArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Validator");
            var validator = (vArg.Value.Value as INamedTypeSymbol)
                ?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (kind == DtoKind.Create) createValidator = validator;
            else updateValidator = validator;
        }

        return BuildDtoClassInfoCore(symbol, kind, nameArg, createValidator, updateValidator);
    } catch { return null; } }

    /// <summary>
    /// Shared core: walks the symbol's properties and builds a <see cref="DtoClassInfo"/>.
    /// Both attribute-driven (<see cref="GetDtoInfo"/>) and fluent-driven (the
    /// IDtoConfigurator pipeline) paths call this with their respective values.
    /// </summary>
    internal static DtoClassInfo? BuildDtoClassInfoCore(
        INamedTypeSymbol symbol, DtoKind kind,
        string? nameOverride, string? createValidator, string? updateValidator)
    { try {

        var seen = new HashSet<string>();
        var autoNested = new List<DtoClassInfo>();
        var properties = CollectProperties(symbol, kind, seen, autoNested);

        // Also collect properties from [IntersectFrom] / [PartialFrom] targets on the same class
        var existingNames = new HashSet<string>(properties.Select(p => p.PropertyName));
        foreach (var a in symbol.GetAttributes())
        {
            INamedTypeSymbol? targetType = null;
            if (a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.IntersectFromAttribute" ||
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.PartialFromAttribute")
            {
                if (a.ConstructorArguments.Length > 0)
                    targetType = a.ConstructorArguments[0].Value as INamedTypeSymbol;
            }
            if (targetType is null) continue;

            foreach (var prop in GetAllProperties(targetType))
            {
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.SetMethod is null || prop.GetMethod is null) continue;
                if (!existingNames.Add(prop.Name)) continue;

                var jsonName = GetJsonName(prop);
                var displayType = prop.Type.ToDisplayString();
                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
                var isValueType = prop.Type.IsValueType;

                properties.Add(new DtoPropertyInfo(
                    prop.Name, jsonName, displayType, isNullable,
                    false, isValueType, 0, 0));
            }
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        // Handle generic type parameters
        var genericParams = "";
        var classNameWithGenerics = symbol.Name;
        if (symbol.IsGenericType && symbol.TypeParameters.Length > 0)
        {
            genericParams = "<" + string.Join(", ", symbol.TypeParameters.Select(tp => tp.Name)) + ">";
            classNameWithGenerics = symbol.Name + genericParams;
        }

        var defaultName = kind switch
        {
            DtoKind.Create => $"Create{symbol.Name}Request{genericParams}",
            DtoKind.Update => $"Update{symbol.Name}Request{genericParams}",
            DtoKind.Combined => $"{symbol.Name}Request{genericParams}",
            _ => $"{symbol.Name}Request{genericParams}"
        };

        var info = new DtoClassInfo(
            classNameWithGenerics,
            ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            nameOverride is not null ? nameOverride + genericParams : defaultName,
            kind,
            properties,
            createValidator,
            updateValidator,
            genericParams);
        info.AutoNestedDtos.AddRange(autoNested);
        return info;
    } catch { return null; } }

    private static ResponseDtoInfo? GetResponseDtoInfo(GeneratorAttributeSyntaxContext context)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;

        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
        if (attr is null) return null;

        var nameArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
        return BuildResponseDtoInfoCore(symbol, nameArg);
    } catch { return null; } }

    /// <summary>
    /// Shared core for response-DTO extraction. Both attribute-driven
    /// (<see cref="GetResponseDtoInfo"/>) and fluent-driven paths reuse this.
    /// Walks all properties, applies <c>[DtoIgnore]</c>/<c>[DtoOnly]</c>/<c>[Flatten]</c>,
    /// detects nested response DTOs, builds a separate ListItem variant when
    /// any property is <c>[DtoIgnore(DtoTarget.List)]</c>.
    /// </summary>
    internal static ResponseDtoInfo? BuildResponseDtoInfoCore(INamedTypeSymbol symbol, string? nameOverride)
    { try {
        var properties = new List<ResponsePropertyInfo>();
        foreach (var prop in GetAllProperties(symbol))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.GetMethod is null) continue;

            var (riIgnore, riOnly) = GetDtoTargetFlags(prop);
            // DtoTarget.Response = 4
            var isIgnoredFromResponse = riIgnore == 31 || (riIgnore & 4) != 0 || (riOnly != 0 && (riOnly & 4) == 0);
            if (isIgnoredFromResponse) continue;

            // Check for [Flatten]
            var hasFlatten = prop.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == FlattenAttributeFqn);

            if (hasFlatten)
            {
                var propType = prop.Type;
                var isNullableRef = propType.NullableAnnotation == NullableAnnotation.Annotated;
                var unwrapped = propType;
                if (unwrapped is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullableF)
                    unwrapped = nullableF.TypeArguments[0];

                if (unwrapped is INamedTypeSymbol flatType)
                {
                    var seenTypes = new HashSet<string>();
                    FlattenRecursive(properties, flatType, prop.Name,
                        $"entity.{prop.Name}", $"x.{prop.Name}",
                        isNullableRef, seenTypes, maxDepth: 5);
                }
                continue;
            }

            var jsonName = GetJsonName(prop);
            var (validationAttrs, _) = GetValidationAttributes(prop);

            // Check if property type (unwrap nullable) has [ResponseDto]
            var propType2 = prop.Type;
            var isNullableRef2 = propType2.NullableAnnotation == NullableAnnotation.Annotated;
            var unwrapped2 = propType2;
            if (unwrapped2 is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullable)
                unwrapped2 = nullable.TypeArguments[0];

            var hasNestedResponseDto = unwrapped2 is INamedTypeSymbol namedType &&
                namedType.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);

            if (!hasNestedResponseDto)
            {
                var displayType = propType2.ToDisplayString();
                properties.Add(new ResponsePropertyInfo(prop.Name, jsonName, displayType, validationAttrs));
                continue;
            }

            var nestedName = ((INamedTypeSymbol)unwrapped2).Name;
            var nestedResponseDtoAttr = unwrapped2.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
            var customName = nestedResponseDtoAttr?.NamedArguments
                .FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            var responseName = customName ?? $"{nestedName}Response";
            var nestedNs = unwrapped2.ContainingNamespace.IsGlobalNamespace
                ? null : unwrapped2.ContainingNamespace.ToDisplayString();
            var fqResponseName = nestedNs is not null ? $"{nestedNs}.{responseName}" : responseName;
            var displayType2 = isNullableRef2 ? $"{fqResponseName}?" : fqResponseName;

            properties.Add(new ResponsePropertyInfo(prop.Name, jsonName, displayType2, validationAttrs,
                isNestedResponse: true, isNullable: isNullableRef2, sourceTypeName: propType2.ToDisplayString(),
                nestedResponseName: fqResponseName));
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        // Check if any property has [DtoIgnore(DtoTarget.List)] or [DtoOnly] without List — generate separate list DTO
        var listIgnoreProps = new HashSet<string>();
        foreach (var prop in GetAllProperties(symbol))
        {
            var (ig, on) = GetDtoTargetFlags(prop);
            // DtoTarget.List = 16
            var ignoredFromList = ig == 31 || (ig & 16) != 0 || (on != 0 && (on & 16) == 0);
            if (ignoredFromList && !(ig == 31)) // only collect props that are specifically list-ignored, not globally ignored
                listIgnoreProps.Add(prop.Name);
        }

        string? listResponseName = null;
        List<ResponsePropertyInfo>? listProperties = null;
        if (listIgnoreProps.Count > 0)
        {
            listResponseName = $"{symbol.Name}ListItem";
            listProperties = properties.Where(p => !listIgnoreProps.Contains(p.PropertyName)).ToList();
        }

        return new ResponseDtoInfo(
            symbol.Name,
            ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            nameOverride ?? $"{symbol.Name}Response",
            properties,
            listResponseName,
            listProperties);
    } catch { return null; } }

    private static DtoForInfo? GetCreateDtoForInfo(GeneratorAttributeSyntaxContext context)
        => GetDtoForInfoCore(context, CreateDtoForAttributeFqn);

    private static DtoForInfo? GetUpdateDtoForInfo(GeneratorAttributeSyntaxContext context)
        => GetDtoForInfoCore(context, UpdateDtoForAttributeFqn);

    private static DtoForInfo? GetDtoForInfoCore(GeneratorAttributeSyntaxContext context, string attributeFqn)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeFqn);

        if (attr is null || attr.ConstructorArguments.Length == 0) return null;
        var targetType = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
        if (targetType is null) return null;

        // Read Ignore array
        var ignoreArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Ignore");
        var ignoreSet = new HashSet<string>();
        if (!ignoreArg.Value.IsNull && ignoreArg.Value.Values.Length > 0)
        {
            foreach (var val in ignoreArg.Value.Values)
            {
                if (val.Value is string s)
                    ignoreSet.Add(s);
            }
        }

        // Read Required array (for CreateDtoFor)
        var requiredArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Required");
        var requiredSet = new HashSet<string>();
        if (!requiredArg.Value.IsNull && requiredArg.Value.Values.Length > 0)
        {
            foreach (var val in requiredArg.Value.Values)
            {
                if (val.Value is string s)
                    requiredSet.Add(s);
            }
        }

        // Per-property renames now go through fluent .Property(p => p.X).RenameTo("y")
        // or [DtoName] on the property — empty map keeps downstream code structurally
        // unchanged without scanning for a class-level rename attribute that no longer exists.
        var renameMap = new Dictionary<string, string>();

        // Handle generics — user's class params map to target's type params
        var genericParams = "";
        var targetFqn = targetType.ToDisplayString();
        if (symbol.IsGenericType && symbol.TypeParameters.Length > 0)
        {
            genericParams = "<" + string.Join(", ", symbol.TypeParameters.Select(tp => tp.Name)) + ">";
            // Replace open generic <> or <,> in target FQN with user's type params
            if (targetType.IsUnboundGenericType || (targetType is { IsGenericType: true } && targetFqn.Contains("<>")))
            {
                targetFqn = targetFqn.Replace("<>", genericParams)
                    .Replace("<,>", genericParams);
                // Handle multi-param: <,,> etc.
                for (int i = 2; i <= 8; i++)
                    targetFqn = targetFqn.Replace("<" + new string(',', i - 1) + ">", genericParams);
            }
        }

        // If unbound generic, get the original definition which has the actual members
        var memberSource = targetType.IsUnboundGenericType ? targetType.OriginalDefinition : targetType;

        var properties = new List<DtoPropertyInfo>();
        foreach (var prop in GetAllProperties(memberSource))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.SetMethod is null || prop.GetMethod is null) continue;
            if (ignoreSet.Contains(prop.Name)) continue;

            var dtoName = renameMap.TryGetValue(prop.Name, out var renamed) ? renamed : prop.Name;
            var jsonName = CamelCase(dtoName);
            var displayType = prop.Type.ToDisplayString();
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            var isValueType = prop.Type.IsValueType;
            var isRequired = requiredSet.Contains(prop.Name) || prop.IsRequired;

            var (validationAttrs, validationRules) = GetValidationAttributes(prop);
            var propInfo = new DtoPropertyInfo(
                dtoName, jsonName, displayType, isNullable,
                isRequired, isValueType, 0, 0,
                sourcePropertyName: dtoName != prop.Name ? prop.Name : null,
                validationAttributes: validationAttrs,
                validationRules: validationRules);

            // DtoFor: check if nested type has explicit [CreateDto]/[UpdateDto] — use its DTO, otherwise plain type
            var unwrappedType = prop.Type;
            if (unwrappedType is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nul2)
                unwrappedType = nul2.TypeArguments[0];

            if (unwrappedType is INamedTypeSymbol namedPropType && !ignoreSet.Contains(prop.Name))
            {
                var propAttrs = namedPropType.GetAttributes();
                var hasExplicitCreate = propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
                var hasExplicitUpdate = propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
                var hasExplicitCombined = propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
                var nestedNs = namedPropType.ContainingNamespace.IsGlobalNamespace ? null : namedPropType.ContainingNamespace.ToDisplayString();

                if (attributeFqn == CreateDtoForAttributeFqn && (hasExplicitCreate || hasExplicitCombined))
                {
                    var createAttr = propAttrs.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn
                        || a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
                    var customName = createAttr?.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
                    var defName = hasExplicitCombined ? $"{namedPropType.Name}Request" : $"Create{namedPropType.Name}Request";
                    propInfo.NestedCreateDtoName = nestedNs is not null ? $"{nestedNs}.{customName ?? defName}" : (customName ?? defName);
                }
                if (attributeFqn == UpdateDtoForAttributeFqn && (hasExplicitUpdate || hasExplicitCombined))
                {
                    var updateAttr = propAttrs.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn
                        || a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
                    var customName = updateAttr?.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
                    var defName = hasExplicitCombined ? $"{namedPropType.Name}Request" : $"Update{namedPropType.Name}Request";
                    propInfo.NestedUpdateDtoName = nestedNs is not null ? $"{nestedNs}.{customName ?? defName}" : (customName ?? defName);
                }
            }

            properties.Add(propInfo);
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var typeKeyword = syntax is RecordDeclarationSyntax ? "record" : "class";
        var className = symbol.Name + genericParams;
        var info = new DtoForInfo(
            className,
            ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            targetFqn,
            properties,
            genericParams,
            typeKeyword);
        return info;
    } catch { return null; } }

    private static QueryDtoInfo? GetQueryDtoInfo(GeneratorAttributeSyntaxContext context)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;

        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == QueryDtoAttributeFqn);

        if (attr is null) return null;

        var nameArg = attr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
        // [QueryDto] defaults Sortable=true — only an explicit false disables it.
        var sortableRaw = attr.NamedArguments.FirstOrDefault(a => a.Key == "Sortable").Value.Value;
        var sortable = sortableRaw is not false;
        var defaultSort = attr.NamedArguments.FirstOrDefault(a => a.Key == "DefaultSort").Value.Value as string;
        var defaultSortDirectionRaw = attr.NamedArguments.FirstOrDefault(a => a.Key == "DefaultSortDirection").Value.Value;
        var defaultSortDirection = defaultSortDirectionRaw is int d ? d : 0;

        return BuildQueryDtoInfoCore(symbol, nameArg, sortable, defaultSort, defaultSortDirection);
    } catch { return null; } }

    /// <summary>
    /// Shared core for query-DTO extraction. Both attribute-driven and fluent paths
    /// reuse this. Walks public properties, applies <c>[DtoIgnore]</c>/<c>[DtoOnly]</c>
    /// + <c>[UiTableColumn(Filterable=false)]</c>, skips complex/nav types, builds
    /// nullable-shaped QueryPropertyInfo + navigation/collection paths for the DSL.
    /// </summary>
    internal static QueryDtoInfo? BuildQueryDtoInfoCore(
        INamedTypeSymbol symbol, string? nameOverride,
        bool sortable, string? defaultSort, int defaultSortDirection)
    { try {
        var properties = new List<QueryPropertyInfo>();
        foreach (var prop in GetAllProperties(symbol))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.GetMethod is null) continue;

            var (qiIgnore, qiOnly) = GetDtoTargetFlags(prop);
            // DtoTarget.Query = 8
            var ignoredFromQuery = qiIgnore == 31 || (qiIgnore & 8) != 0 || (qiOnly != 0 && (qiOnly & 8) == 0);
            if (ignoredFromQuery) continue;

            // Bridge: [UiTableColumn(Filterable = false)] → skip from query
            var tableColAttr = prop.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.UI.UiTableColumnAttribute");
            if (tableColAttr is not null)
            {
                var filterableArg = tableColAttr.NamedArguments.FirstOrDefault(a => a.Key == "Filterable");
                if (filterableArg.Key is not null && filterableArg.Value.Value is false)
                    continue;
            }

            // Skip complex/navigation types — query parameters must be primitives
            var propType = prop.Type;
            if (propType is INamedTypeSymbol nts && nts.NullableAnnotation == NullableAnnotation.Annotated
                && nts.TypeArguments.Length == 1)
                propType = nts.TypeArguments[0]; // unwrap Nullable<T>

            if (propType.TypeKind == TypeKind.Class
                && propType.SpecialType == SpecialType.None
                && propType.ToDisplayString() != "string")
                continue;

            if (propType.TypeKind == TypeKind.Interface || propType.TypeKind == TypeKind.Array)
                continue;

            var jsonName = GetJsonName(prop);
            var displayType = prop.Type.ToDisplayString();
            var isValueType = prop.Type.IsValueType;
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            // Make nullable version of the type for query DTO
            var nullableType = isNullable || !isValueType
                ? displayType + (isNullable ? "" : "?")
                : displayType + "?";

            properties.Add(new QueryPropertyInfo(prop.Name, jsonName, displayType, nullableType, isValueType));
        }

        // Collect navigation property paths for DSL filtering (1 level deep)
        var navigationPaths = new List<QueryNavigationPath>();
        var collectionPaths = new List<QueryCollectionPath>();
        foreach (var prop in GetAllProperties(symbol))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.GetMethod is null) continue;

            // Detect navigation properties: [OneToOne] or non-collection class types (not string)
            var propType = prop.Type;

            // Check for [OneToOne] attribute (single navigation)
            var hasOneToOne = prop.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.OneToOneAttribute");

            // Also detect by type: non-collection, non-string class with public properties
            var isNavigation = hasOneToOne;
            if (!isNavigation && propType is INamedTypeSymbol navNts)
            {
                var unwrapped = navNts.NullableAnnotation == NullableAnnotation.Annotated && navNts.TypeArguments.Length == 1
                    ? navNts.TypeArguments[0] as INamedTypeSymbol
                    : navNts;
                if (unwrapped is not null
                    && unwrapped.TypeKind == TypeKind.Class
                    && unwrapped.SpecialType == SpecialType.None
                    && unwrapped.ToDisplayString() != "string"
                    && !unwrapped.AllInterfaces.Any(i => i.ToDisplayString().StartsWith("System.Collections")))
                {
                    isNavigation = true;
                    propType = unwrapped;
                }
            }

            // Check for OneToMany (ICollection<T>)
            var hasOneToMany = prop.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.OneToManyAttribute");
            if (hasOneToMany && propType is INamedTypeSymbol collNts2)
            {
                var elementType = collNts2.AllInterfaces
                    .Concat(new[] { collNts2 })
                    .Where(i => i.IsGenericType && (i.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.ICollection") || i.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable")))
                    .SelectMany(i => i.TypeArguments)
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault();
                if (elementType is null && collNts2.TypeArguments.Length == 1)
                    elementType = collNts2.TypeArguments[0] as INamedTypeSymbol;

                if (elementType is not null)
                {
                    collectionPaths.Add(new QueryCollectionPath(
                        $"{prop.Name.ToLowerInvariant()}.count", prop.Name, elementType.ToDisplayString(), "Count", "int", true));

                    foreach (var subProp in GetAllProperties(elementType))
                    {
                        if (subProp.DeclaredAccessibility != Accessibility.Public) continue;
                        if (subProp.GetMethod is null) continue;
                        var st = subProp.Type;
                        var stv = st.IsValueType;
                        if (st is INamedTypeSymbol sn && sn.NullableAnnotation == NullableAnnotation.Annotated && sn.TypeArguments.Length == 1)
                            st = sn.TypeArguments[0];
                        if (st.TypeKind == TypeKind.Class && st.SpecialType == SpecialType.None && st.ToDisplayString() != "string") continue;
                        if (st.TypeKind == TypeKind.Interface || st.TypeKind == TypeKind.Array) continue;

                        collectionPaths.Add(new QueryCollectionPath(
                            $"{prop.Name.ToLowerInvariant()}.{subProp.Name.ToLowerInvariant()}", prop.Name, elementType.ToDisplayString(), subProp.Name, subProp.Type.ToDisplayString(), stv));
                        collectionPaths.Add(new QueryCollectionPath(
                            $"{prop.Name.ToLowerInvariant()}.any.{subProp.Name.ToLowerInvariant()}", prop.Name, elementType.ToDisplayString(), subProp.Name, subProp.Type.ToDisplayString(), stv));
                        collectionPaths.Add(new QueryCollectionPath(
                            $"{prop.Name.ToLowerInvariant()}.all.{subProp.Name.ToLowerInvariant()}", prop.Name, elementType.ToDisplayString(), subProp.Name, subProp.Type.ToDisplayString(), stv));
                    }
                }
                continue;
            }

            if (!isNavigation) continue;

            // Extract sub-properties of the navigation type
            var navTypeSymbol = propType as INamedTypeSymbol;
            if (navTypeSymbol is null) continue;

            foreach (var subProp in GetAllProperties(navTypeSymbol))
            {
                if (subProp.DeclaredAccessibility != Accessibility.Public) continue;
                if (subProp.GetMethod is null) continue;

                var subType = subProp.Type;
                var subIsValueType = subType.IsValueType;

                // Only include primitive/filterable sub-properties
                if (subType is INamedTypeSymbol subNts && subNts.NullableAnnotation == NullableAnnotation.Annotated
                    && subNts.TypeArguments.Length == 1)
                    subType = subNts.TypeArguments[0];

                if (subType.TypeKind == TypeKind.Class && subType.SpecialType == SpecialType.None
                    && subType.ToDisplayString() != "string")
                    continue;
                if (subType.TypeKind == TypeKind.Interface || subType.TypeKind == TypeKind.Array)
                    continue;

                var dotPath = $"{prop.Name.ToLowerInvariant()}.{subProp.Name.ToLowerInvariant()}";
                var exprPath = $"{prop.Name}.{subProp.Name}";
                navigationPaths.Add(new QueryNavigationPath(dotPath, exprPath, subProp.Type.ToDisplayString(), subIsValueType));
            }
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        return new QueryDtoInfo(
            symbol.Name, ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            nameOverride ?? $"{symbol.Name}Query",
            properties,
            sortable,
            defaultSort,
            defaultSortDirection,
            navigationPaths,
            collectionPaths: collectionPaths);
    } catch { return null; } }

    private static IEnumerable<IPropertySymbol> GetAllProperties(INamedTypeSymbol symbol)
    {
        var seen = new HashSet<string>();
        var current = symbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop && seen.Add(prop.Name))
                    yield return prop;
            }
            current = current.BaseType;
            if (current?.SpecialType == SpecialType.System_Object) break;
        }
    }

    private static List<DtoPropertyInfo> CollectProperties(INamedTypeSymbol symbol)
        => CollectProperties(symbol, null, null, null);

    private static List<DtoPropertyInfo> CollectProperties(INamedTypeSymbol symbol, DtoKind? kind, HashSet<string>? seen, List<DtoClassInfo>? autoNested)
    {
        // No class-level rename map — per-property renames go through fluent
        // .Property(p => p.X).RenameTo("y") or [DtoName] applied per property.
        var renameMap = new Dictionary<string, string>();

        var properties = new List<DtoPropertyInfo>();

        foreach (var prop in GetAllProperties(symbol))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.SetMethod is null || prop.GetMethod is null) continue;

            var (ignoreTargets, onlyTargets) = GetDtoTargetFlags(prop);
            // [DtoIgnore] without args → ignoreTargets = 31 (All). Skip entirely.
            if (ignoreTargets == 31) continue;

            var dtoName = renameMap.TryGetValue(prop.Name, out var renamed) ? renamed : prop.Name;
            var jsonName = renameMap.ContainsKey(prop.Name) ? CamelCase(dtoName) : GetJsonName(prop);
            var displayType = prop.Type.ToDisplayString();
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            var isValueType = prop.Type.IsValueType;
            var isRequired = prop.IsRequired;
            var (validationAttrs, validationRules) = GetValidationAttributes(prop);

            // Check for [Flatten] — expand child properties
            var hasFlatten = prop.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == FlattenAttributeFqn);

            if (hasFlatten)
            {
                var propType = prop.Type;
                var isNullableRef = propType.NullableAnnotation == NullableAnnotation.Annotated;
                var unwrapped = propType;
                if (unwrapped is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullableF)
                    unwrapped = nullableF.TypeArguments[0];

                if (unwrapped is INamedTypeSymbol flatType)
                {
                    var seenTypes = new HashSet<string>();
                    FlattenForDto(properties, flatType, prop.Name, prop.Name,
                        isNullableRef, seenTypes, 5, ignoreTargets, onlyTargets);
                }
                continue;
            }

            var propInfo = new DtoPropertyInfo(
                dtoName,
                jsonName,
                displayType,
                isNullable,
                isRequired,
                isValueType,
                ignoreTargets,
                onlyTargets,
                sourcePropertyName: dtoName != prop.Name ? prop.Name : null,
                validationAttributes: validationAttrs,
                validationRules: validationRules);

            // Detect nested complex types for auto-recursive DTO generation
            if (kind is null)
            {
                properties.Add(propInfo);
                continue;
            }

            var unwrappedType = prop.Type;
            if (unwrappedType is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nul)
                unwrappedType = nul.TypeArguments[0];

            if (unwrappedType is INamedTypeSymbol namedPropType && IsComplexType(namedPropType))
            {
                var typeFqn = namedPropType.ToDisplayString();
                var propAttrs = namedPropType.GetAttributes();
                var hasExplicitCreate = propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
                var hasExplicitUpdate = propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
                var hasExplicitCombined = propAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
                var nestedNs = namedPropType.ContainingNamespace.IsGlobalNamespace ? null : namedPropType.ContainingNamespace.ToDisplayString();

                // Resolve nested DTO name — explicit attribute wins, otherwise auto-generate
                if (kind == DtoKind.Create || kind == DtoKind.Combined)
                {
                    if (hasExplicitCreate || hasExplicitCombined)
                    {
                        var createAttr = propAttrs.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn
                            || a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
                        var customName = createAttr?.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
                        var defName = hasExplicitCombined ? $"{namedPropType.Name}Request" : $"Create{namedPropType.Name}Request";
                        propInfo.NestedCreateDtoName = nestedNs is not null ? $"{nestedNs}.{customName ?? defName}" : (customName ?? defName);
                    }
                    else if (seen is not null && autoNested is not null)
                    {
                        // Auto-generate nested DTO
                        var nestedDtoName = $"Create{namedPropType.Name}Request";
                        var fqNestedDtoName = nestedNs is not null ? $"{nestedNs}.{nestedDtoName}" : nestedDtoName;
                        propInfo.NestedCreateDtoName = fqNestedDtoName;

                        if (seen.Add(typeFqn + ":Create"))
                        {
                            var nestedProps = CollectProperties(namedPropType, DtoKind.Create, seen, autoNested);
                            autoNested.Add(new DtoClassInfo(
                                namedPropType.Name, nestedNs,
                                typeFqn.Replace(".", "_"),
                                nestedDtoName, DtoKind.Create,
                                nestedProps, null, null));
                        }
                    }
                }

                if (kind == DtoKind.Update || kind == DtoKind.Combined)
                {
                    if (hasExplicitUpdate || hasExplicitCombined)
                    {
                        var updateAttr = propAttrs.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn
                            || a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
                        var customName = updateAttr?.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
                        var defName = hasExplicitCombined ? $"{namedPropType.Name}Request" : $"Update{namedPropType.Name}Request";
                        propInfo.NestedUpdateDtoName = nestedNs is not null ? $"{nestedNs}.{customName ?? defName}" : (customName ?? defName);
                    }
                    else if (seen is not null && autoNested is not null)
                    {
                        var nestedDtoName = $"Update{namedPropType.Name}Request";
                        var fqNestedDtoName = nestedNs is not null ? $"{nestedNs}.{nestedDtoName}" : nestedDtoName;
                        propInfo.NestedUpdateDtoName = fqNestedDtoName;

                        if (seen.Add(typeFqn + ":Update"))
                        {
                            var nestedProps = CollectProperties(namedPropType, DtoKind.Update, seen, autoNested);
                            autoNested.Add(new DtoClassInfo(
                                namedPropType.Name, nestedNs,
                                typeFqn.Replace(".", "_"),
                                nestedDtoName, DtoKind.Update,
                                nestedProps, null, null));
                        }
                    }
                }
            }

            properties.Add(propInfo);
        }

        return properties;
    }

    private static void FlattenRecursive(
        List<ResponsePropertyInfo> properties,
        INamedTypeSymbol type,
        string namePrefix,
        string entityPath,
        string projectionPath,
        bool parentNullable,
        HashSet<string> seenTypes,
        int maxDepth)
    {
        if (maxDepth <= 0) return;
        var typeFqn = type.ToDisplayString();
        if (!seenTypes.Add(typeFqn)) return; // cycle detection

        var nullOp = parentNullable ? "?" : "";

        foreach (var childProp in GetAllProperties(type))
        {
            if (childProp.DeclaredAccessibility != Accessibility.Public) continue;
            if (childProp.GetMethod is null) continue;

            var flatName = $"{namePrefix}{childProp.Name}";
            var flatJsonName = CamelCase(flatName);
            var childEntityPath = $"{entityPath}{nullOp}.{childProp.Name}";
            var childProjectionPath = $"{projectionPath}{(parentNullable ? "!" : "")}.{childProp.Name}";

            // Check if child is complex type → recurse
            var childType = childProp.Type;
            var childNullable = childType.NullableAnnotation == NullableAnnotation.Annotated;
            var unwrappedChild = childType;
            if (unwrappedChild is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nul)
                unwrappedChild = nul.TypeArguments[0];

            if (unwrappedChild is INamedTypeSymbol namedChild && IsComplexType(namedChild))
            {
                // Recurse — flatten deeper
                FlattenRecursive(properties, namedChild, flatName,
                    childEntityPath, childProjectionPath,
                    parentNullable || childNullable, seenTypes, maxDepth - 1);
                continue;
            }

            // Leaf property — emit
            var childDisplayType = childType.ToDisplayString();
            if (parentNullable && !childType.IsValueType &&
                childType.NullableAnnotation != NullableAnnotation.Annotated)
                childDisplayType += "?";
            if (parentNullable && childType.IsValueType &&
                !(childType is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T }))
                childDisplayType += "?";

            var (validationAttrs, _) = GetValidationAttributes(childProp);
            properties.Add(new ResponsePropertyInfo(flatName, flatJsonName, childDisplayType, validationAttrs,
                flattenSource: childEntityPath,
                flattenProjection: childProjectionPath));
        }

        seenTypes.Remove(typeFqn); // allow same type on different branches
    }

    private static void FlattenForDto(
        List<DtoPropertyInfo> properties,
        INamedTypeSymbol type,
        string namePrefix,
        string entityPath,
        bool parentNullable,
        HashSet<string> seenTypes,
        int maxDepth,
        int ignoreTargets,
        int onlyTargets)
    {
        if (maxDepth <= 0) return;
        var typeFqn = type.ToDisplayString();
        if (!seenTypes.Add(typeFqn)) return;

        foreach (var childProp in GetAllProperties(type))
        {
            if (childProp.DeclaredAccessibility != Accessibility.Public) continue;
            if (childProp.SetMethod is null || childProp.GetMethod is null) continue;

            var flatName = $"{namePrefix}{childProp.Name}";
            var flatJsonName = CamelCase(flatName);
            var childEntityPath = $"{entityPath}.{childProp.Name}";

            var childType = childProp.Type;
            var childNullable = childType.NullableAnnotation == NullableAnnotation.Annotated;
            var unwrappedChild = childType;
            if (unwrappedChild is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nul)
                unwrappedChild = nul.TypeArguments[0];

            if (unwrappedChild is INamedTypeSymbol namedChild && IsComplexType(namedChild))
            {
                FlattenForDto(properties, namedChild, flatName, childEntityPath,
                    parentNullable || childNullable, seenTypes, maxDepth - 1, ignoreTargets, onlyTargets);
                continue;
            }

            var childDisplayType = childType.ToDisplayString();
            var childIsValueType = childType.IsValueType;
            // Make nullable if parent is nullable
            if (parentNullable && !childIsValueType && childType.NullableAnnotation != NullableAnnotation.Annotated)
                childDisplayType += "?";
            if (parentNullable && childIsValueType &&
                !(childType is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T }))
                childDisplayType += "?";

            var (childValidationAttrs, childValidationRules) = GetValidationAttributes(childProp);
            var childIsNullable = childNullable || parentNullable;

            var pi = new DtoPropertyInfo(
                flatName, flatJsonName, childDisplayType,
                childIsNullable, false, childIsValueType,
                ignoreTargets, onlyTargets,
                validationAttributes: childValidationAttrs,
                validationRules: childValidationRules);
            pi.FlattenEntityPath = childEntityPath;
            properties.Add(pi);
        }

        seenTypes.Remove(typeFqn);
    }

    private static bool IsComplexType(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None) return false;
        if (type.TypeKind == TypeKind.Enum) return false;
        if (type is IArrayTypeSymbol) return false;
        var display = type.ToDisplayString();
        if (display is "string" or "System.String"
            or "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly"
            or "System.Guid" or "System.TimeSpan" or "System.Uri" or "decimal" or "System.Decimal")
            return false;
        // Skip collection types
        if (type.AllInterfaces.Any(i =>
            i.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable<") ||
            i.ToDisplayString().StartsWith("System.Collections.IEnumerable")))
            return false;
        if (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
            return true;
        return false;
    }

    private static string GetJsonName(IPropertySymbol prop)
    {
        var dtoNameAttr = prop.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == DtoNameAttributeFqn);
        if (dtoNameAttr is not null && dtoNameAttr.ConstructorArguments.Length > 0)
            return dtoNameAttr.ConstructorArguments[0].Value?.ToString() ?? CamelCase(prop.Name);

        var jsonPropAttr = prop.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonPropertyNameAttribute");
        if (jsonPropAttr is not null && jsonPropAttr.ConstructorArguments.Length > 0)
            return jsonPropAttr.ConstructorArguments[0].Value?.ToString() ?? CamelCase(prop.Name);

        return CamelCase(prop.Name);
    }

    private static readonly HashSet<string> ValidationNamespaces = new()
    {
        "System.ComponentModel.DataAnnotations",
        "ZibStack.NET.Validation"
    };

    private static readonly Dictionary<string, ValidationRuleKind> KnownValidationAttributes = new()
    {
        // System.ComponentModel.DataAnnotations
        { "System.ComponentModel.DataAnnotations.MinLengthAttribute", ValidationRuleKind.MinLength },
        { "System.ComponentModel.DataAnnotations.MaxLengthAttribute", ValidationRuleKind.MaxLength },
        { "System.ComponentModel.DataAnnotations.StringLengthAttribute", ValidationRuleKind.StringLength },
        { "System.ComponentModel.DataAnnotations.RangeAttribute", ValidationRuleKind.Range },
        { "System.ComponentModel.DataAnnotations.EmailAddressAttribute", ValidationRuleKind.Email },
        { "System.ComponentModel.DataAnnotations.UrlAttribute", ValidationRuleKind.Url },
        { "System.ComponentModel.DataAnnotations.RegularExpressionAttribute", ValidationRuleKind.Regex },
        { "System.ComponentModel.DataAnnotations.PhoneAttribute", ValidationRuleKind.Phone },
        // ZibStack.NET.Validation
        { "ZibStack.NET.Validation.ZMinLengthAttribute", ValidationRuleKind.MinLength },
        { "ZibStack.NET.Validation.ZMaxLengthAttribute", ValidationRuleKind.MaxLength },
        { "ZibStack.NET.Validation.ZRangeAttribute", ValidationRuleKind.Range },
        { "ZibStack.NET.Validation.ZEmailAttribute", ValidationRuleKind.Email },
        { "ZibStack.NET.Validation.ZUrlAttribute", ValidationRuleKind.Url },
        { "ZibStack.NET.Validation.ZMatchAttribute", ValidationRuleKind.Regex },
        { "ZibStack.NET.Validation.ZNotEmptyAttribute", ValidationRuleKind.NotEmpty },
    };

    private static (List<string> Attributes, List<ValidationRule> Rules) GetValidationAttributes(IPropertySymbol prop)
    {
        var attrs = new List<string>();
        var rules = new List<ValidationRule>();

        foreach (var attr in prop.GetAttributes())
        {
            var ns = attr.AttributeClass?.ContainingNamespace?.ToDisplayString();
            if (ns is null || !ValidationNamespaces.Contains(ns)) continue;

            var name = attr.AttributeClass!.ToDisplayString();
            var sb = new StringBuilder();
            sb.Append($"[{name}");

            var args = new List<string>();
            foreach (var arg in attr.ConstructorArguments)
            {
                args.Add(FormatTypedConstant(arg));
            }
            foreach (var named in attr.NamedArguments)
            {
                args.Add($"{named.Key} = {FormatTypedConstant(named.Value)}");
            }

            if (args.Count > 0)
                sb.Append($"({string.Join(", ", args)})");

            sb.Append("]");
            attrs.Add(sb.ToString());

            if (KnownValidationAttributes.TryGetValue(name, out var ruleKind))
            {
                var rule = ExtractRule(ruleKind, attr);
                if (rule is not null) rules.Add(rule);
            }
        }
        return (attrs, rules);
    }

    private static ValidationRule? ExtractRule(ValidationRuleKind kind, AttributeData attr)
    {
        string? message = null;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key is "ErrorMessage" or "Message")
                message = named.Value.Value as string;
        }

        switch (kind)
        {
            case ValidationRuleKind.MinLength:
            {
                if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int len)
                    return new ValidationRule(kind, intParam1: len, message: message);
                // ZibStack.NET.Validation uses "Length" named arg sometimes
                foreach (var n in attr.NamedArguments)
                    if (n.Key == "Length" && n.Value.Value is int nLen)
                        return new ValidationRule(kind, intParam1: nLen, message: message);
                return null;
            }
            case ValidationRuleKind.MaxLength:
            {
                if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int len)
                    return new ValidationRule(kind, intParam1: len, message: message);
                foreach (var n in attr.NamedArguments)
                    if (n.Key == "Length" && n.Value.Value is int nLen)
                        return new ValidationRule(kind, intParam1: nLen, message: message);
                return null;
            }
            case ValidationRuleKind.StringLength:
            {
                int? max = attr.ConstructorArguments.Length >= 1 ? attr.ConstructorArguments[0].Value as int? : null;
                int? min = null;
                foreach (var n in attr.NamedArguments)
                    if (n.Key == "MinimumLength" && n.Value.Value is int m)
                        min = m;
                if (max is not null)
                    return new ValidationRule(kind, intParam1: max, intParam2: min, message: message);
                return null;
            }
            case ValidationRuleKind.Range:
            {
                double? min = null, max = null;
                if (attr.ConstructorArguments.Length >= 2)
                {
                    min = Convert.ToDouble(attr.ConstructorArguments[0].Value);
                    max = Convert.ToDouble(attr.ConstructorArguments[1].Value);
                }
                foreach (var n in attr.NamedArguments)
                {
                    if (n.Key == "Min" || n.Key == "Minimum") min = Convert.ToDouble(n.Value.Value);
                    if (n.Key == "Max" || n.Key == "Maximum") max = Convert.ToDouble(n.Value.Value);
                }
                if (min is not null && max is not null)
                    return new ValidationRule(kind, doubleParam1: min, doubleParam2: max, message: message);
                return null;
            }
            case ValidationRuleKind.Regex:
            {
                string? pattern = null;
                if (attr.ConstructorArguments.Length >= 1)
                    pattern = attr.ConstructorArguments[0].Value as string;
                foreach (var n in attr.NamedArguments)
                    if (n.Key == "Pattern") pattern = n.Value.Value as string;
                if (pattern is not null)
                    return new ValidationRule(kind, stringParam: pattern, message: message);
                return null;
            }
            case ValidationRuleKind.Email:
            case ValidationRuleKind.Url:
            case ValidationRuleKind.NotEmpty:
            case ValidationRuleKind.Phone:
                return new ValidationRule(kind, message: message);
            default:
                return null;
        }
    }

    private static string FormatTypedConstant(TypedConstant tc)
    {
        if (tc.Kind == TypedConstantKind.Array)
        {
            var items = tc.Values.Select(FormatTypedConstant);
            return $"new[] {{ {string.Join(", ", items)} }}";
        }
        if (tc.Value is string s)
            return $"\"{s}\"";
        if (tc.Value is bool b)
            return b ? "true" : "false";
        return tc.Value?.ToString() ?? "null";
    }

    private static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>
    /// Reads [DtoIgnore] and [DtoOnly] from a property and returns the flags.
    /// DtoTarget: Create=1, Update=2, Response=4, Query=8, List=16, All=31.
    /// Returns (ignoreTargets, onlyTargets). At most one is non-zero.
    /// [DtoIgnore] without args → ignoreTargets=31 (All).
    /// </summary>
    private static (int ignoreTargets, int onlyTargets) GetDtoTargetFlags(IPropertySymbol prop)
    {
        int ignoreTargets = 0;
        int onlyTargets = 0;

        foreach (var attr in prop.GetAttributes())
        {
            var fqn = attr.AttributeClass?.ToDisplayString();
            if (fqn == DtoIgnoreAttributeFqn)
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int target)
                    ignoreTargets |= target;
                else
                    ignoreTargets = 31; // DtoTarget.All
            }
            else if (fqn == DtoOnlyAttributeFqn)
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int only)
                    onlyTargets |= only;
            }
        }

        return (ignoreTargets, onlyTargets);
    }
}
