using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Dto;

public partial class DtoGenerator
{
    private static CrudApiInfo? GetCrudApiInfo(GeneratorAttributeSyntaxContext context)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;

        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudApiAttributeFqn
                              || a.AttributeClass?.ToDisplayString() == UiImTiredOfCrudAttributeFqn);
        if (attr is null) return null;

        var route = attr.NamedArguments.FirstOrDefault(a => a.Key == "Route").Value.Value as string;
        var keyProperty = attr.NamedArguments.FirstOrDefault(a => a.Key == "KeyProperty").Value.Value as string ?? "Id";
        var operationsRaw = attr.NamedArguments.FirstOrDefault(a => a.Key == "Operations").Value.Value;
        var operations = operationsRaw is int ops ? ops : 31; // CrudOperations.All = 31
        var styleRaw = attr.NamedArguments.FirstOrDefault(a => a.Key == "Style").Value.Value;
        var style = styleRaw is int s ? s : 0; // ApiStyle.MinimalApi = 0
        var authorizePolicy = attr.NamedArguments.FirstOrDefault(a => a.Key == "AuthorizePolicy").Value.Value as string;
        var getByIdPolicy = attr.NamedArguments.FirstOrDefault(a => a.Key == "GetByIdPolicy").Value.Value as string;
        var getListPolicy = attr.NamedArguments.FirstOrDefault(a => a.Key == "GetListPolicy").Value.Value as string;
        var createPolicy = attr.NamedArguments.FirstOrDefault(a => a.Key == "CreatePolicy").Value.Value as string;
        var updatePolicy = attr.NamedArguments.FirstOrDefault(a => a.Key == "UpdatePolicy").Value.Value as string;
        var deletePolicy = attr.NamedArguments.FirstOrDefault(a => a.Key == "DeletePolicy").Value.Value as string;
        var softDeleteRaw = attr.NamedArguments.FirstOrDefault(a => a.Key == "SoftDelete").Value.Value;
        var softDelete = softDeleteRaw is true;
        var concurrency = attr.NamedArguments.FirstOrDefault(a => a.Key == "Concurrency").Value.Value is true;
        var hasUserRowVersion = concurrency && GetAllProperties(symbol).Any(p => p.Name == "RowVersion");
        var audit = attr.NamedArguments.FirstOrDefault(a => a.Key == "Audit").Value.Value is true;
        var auditFieldsToGenerate = new List<string>();
        if (audit)
        {
            var existing = new HashSet<string>(GetAllProperties(symbol).Select(p => p.Name));
            foreach (var f in new[] { "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy" })
                if (!existing.Contains(f)) auditFieldsToGenerate.Add(f);
        }
        var signalR = symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "SignalRHubAttribute");

        // Resolve key property type
        var keyProp = GetAllProperties(symbol).FirstOrDefault(p => p.Name == keyProperty);
        if (keyProp is null) return null;
        var keyTypeName = keyProp.Type.ToDisplayString();

        // Auto-generate route from class name if not specified
        if (route is null)
        {
            var routePrefix = attr.NamedArguments.FirstOrDefault(a => a.Key == "RoutePrefix").Value.Value as string;
            var name = symbol.Name;
            route = routePrefix is not null
                ? "api/" + routePrefix.TrimEnd('/') + "/" + Pluralize(CamelCase(name))
                : "api/" + Pluralize(CamelCase(name));
        }

        // Cross-reference DTO attributes on the same entity
        var allAttrs = symbol.GetAttributes();

        var hasCreateDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
        var hasUpdateDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
        var hasCombined = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
        var hasResponseDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
        var hasQueryDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == QueryDtoAttributeFqn);

        // Resolve DTO type names — [CrudApi] auto-implies missing DTO attributes
        string? createRequestName = null;
        string? updateRequestName = null;
        string? responseName = null;
        string? queryName = null;

        if (hasCreateDto)
        {
            var cAttr = allAttrs.First(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
            var customName = cAttr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            createRequestName = customName ?? $"Create{symbol.Name}Request";
        }
        else if (!hasCombined)
        {
            // Auto-imply CreateDto
            createRequestName = $"Create{symbol.Name}Request";
        }

        if (hasUpdateDto)
        {
            var uAttr = allAttrs.First(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
            var customName = uAttr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            updateRequestName = customName ?? $"Update{symbol.Name}Request";
        }
        else if (!hasCombined)
        {
            // Auto-imply UpdateDto
            updateRequestName = $"Update{symbol.Name}Request";
        }

        if (hasCombined)
        {
            var comAttr = allAttrs.First(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
            var customName = comAttr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            var combinedName = customName ?? $"{symbol.Name}Request";
            createRequestName = combinedName;
            updateRequestName = combinedName;
        }

        if (hasResponseDto)
        {
            var rAttr = allAttrs.First(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
            var customName = rAttr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            responseName = customName ?? $"{symbol.Name}Response";
        }
        else
        {
            // Auto-imply ResponseDto
            hasResponseDto = true;
            responseName = $"{symbol.Name}Response";
        }

        if (hasQueryDto)
        {
            var qAttr = allAttrs.First(a => a.AttributeClass?.ToDisplayString() == QueryDtoAttributeFqn);
            var customName = qAttr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            queryName = customName ?? $"{symbol.Name}Query";
        }
        else
        {
            // Auto-imply QueryDto
            hasQueryDto = true;
            queryName = $"{symbol.Name}Query";
        }

        // Bridge: extract [ColumnPermission("Column", "permission")] from class
        var columnPermissions = new Dictionary<string, string>();
        var listColumnPermissions = new Dictionary<string, string>();
        foreach (var a in allAttrs)
        {
            if (a.AttributeClass?.ToDisplayString() == "ZibStack.NET.UI.ColumnPermissionAttribute"
                && a.ConstructorArguments.Length >= 2
                && a.ConstructorArguments[0].Value is string colName
                && a.ConstructorArguments[1].Value is string colPerm)
            {
                // Only keep columns that actually exist on the response DTO — restricted
                // columns ignored from the response can't (and don't need to) be masked.
                var colProp = GetAllProperties(symbol).FirstOrDefault(p => p.Name == colName);
                if (colProp is null) continue;
                var (cig, con) = GetDtoTargetFlags(colProp);
                // DtoTarget.Response = 4
                var ignoredFromResponse = cig == 31 || (cig & 4) != 0 || (con != 0 && (con & 4) == 0);
                if (ignoredFromResponse) continue;
                columnPermissions[colName] = colPerm;
                // DtoTarget.List = 16
                var ignoredFromList = (cig & 16) != 0 || (con != 0 && (con & 16) == 0);
                if (!ignoredFromList)
                    listColumnPermissions[colName] = colPerm;
            }
        }

        // Detect [DtoIgnore(DtoTarget.List)] on any property → separate list response DTO
        var hasListIgnore = GetAllProperties(symbol).Any(p =>
        {
            var (ig, on) = GetDtoTargetFlags(p);
            // DtoTarget.List = 16 — if specifically ignored from list (but not from everything)
            return ig != 31 && ((ig & 16) != 0 || (on != 0 && (on & 16) == 0));
        });
        var listResponseName = hasListIgnore ? $"{symbol.Name}ListItem" : (string?)null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        return new CrudApiInfo(
            symbol.Name,
            ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            route,
            keyProperty,
            keyTypeName,
            operations,
            style,
            authorizePolicy,
            createRequestName,
            updateRequestName,
            responseName,
            queryName,
            hasCombined,
            hasResponseDto,
            hasQueryDto,
            getByIdPolicy,
            getListPolicy,
            createPolicy,
            updatePolicy,
            deletePolicy,
            listResponseName,
            columnPermissions) { SoftDelete = softDelete, SignalR = signalR, Concurrency = concurrency, HasUserRowVersion = hasUserRowVersion, Audit = audit, AuditFieldsToGenerate = auditFieldsToGenerate, ListColumnPermissions = listColumnPermissions };
    } catch { return null; } }

    // ─── Auto-implied DTOs from [CrudApi] ──────────────────────────────

    private static CrudImpliedDtos? GetCrudImpliedDtos(GeneratorAttributeSyntaxContext context)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var allAttrs = symbol.GetAttributes();

        var hasCreateDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
        var hasUpdateDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
        var hasCombined = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
        var hasResponseDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
        var hasQueryDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == QueryDtoAttributeFqn);

        // If all explicit DTOs are present, nothing to auto-generate
        var needsCreate = !hasCreateDto && !hasCombined;
        var needsUpdate = !hasUpdateDto && !hasCombined;
        var needsResponse = !hasResponseDto;
        var needsQuery = !hasQueryDto;
        if (!needsCreate && !needsUpdate && !needsResponse && !needsQuery) return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null : symbol.ContainingNamespace.ToDisplayString();
        var fqn = SanitizeHintName(symbol.ToDisplayString().Replace(".", "_"));

        var result = new CrudImpliedDtos();

        if (needsCreate)
        {
            var seen = new HashSet<string>();
            var autoNested = new List<DtoClassInfo>();
            var properties = CollectProperties(symbol, DtoKind.Create, seen, autoNested);
            var info = new DtoClassInfo(symbol.Name, ns, fqn,
                $"Create{symbol.Name}Request", DtoKind.Create, properties, null, null);
            info.AutoNestedDtos.AddRange(autoNested);
            result.CreateDtos.Add(info);
        }

        if (needsUpdate)
        {
            var seen = new HashSet<string>();
            var autoNested = new List<DtoClassInfo>();
            var properties = CollectProperties(symbol, DtoKind.Update, seen, autoNested);
            var info = new DtoClassInfo(symbol.Name, ns, fqn,
                $"Update{symbol.Name}Request", DtoKind.Update, properties, null, null);
            info.AutoNestedDtos.AddRange(autoNested);
            result.UpdateDtos.Add(info);
        }

        if (needsResponse)
        {
            var properties = new List<ResponsePropertyInfo>();
            foreach (var prop in GetAllProperties(symbol))
            {
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetMethod is null) continue;

                var (crIg, crOn) = GetDtoTargetFlags(prop);
                // DtoTarget.Response = 4
                var hasResponseIgnore = crIg == 31 || (crIg & 4) != 0 || (crOn != 0 && (crOn & 4) == 0);
                if (hasResponseIgnore) continue;

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

                // Check nested [ResponseDto]
                var propType2 = prop.Type;
                var isNullableRef2 = propType2.NullableAnnotation == NullableAnnotation.Annotated;
                var unwrapped2 = propType2;
                if (unwrapped2 is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullable)
                    unwrapped2 = nullable.TypeArguments[0];

                var hasNestedResponseDto = unwrapped2 is INamedTypeSymbol namedType &&
                    namedType.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);

                if (!hasNestedResponseDto)
                {
                    // Check [DtoName] on property for rename
                    var dtoNameAttr = prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DtoNameAttributeFqn);
                    var dtoName = dtoNameAttr?.ConstructorArguments.Length > 0 ? dtoNameAttr.ConstructorArguments[0].Value as string : null;
                    var propName = dtoName ?? prop.Name;
                    var propJsonName = dtoName != null ? (char.ToLowerInvariant(dtoName[0]) + dtoName.Substring(1)) : jsonName;
                    properties.Add(new ResponsePropertyInfo(propName, propJsonName, propType2.ToDisplayString(), validationAttrs, sourcePropertyName: prop.Name));
                }
                else
                {
                    var nestedName = ((INamedTypeSymbol)unwrapped2).Name;
                    var nestedResponseDtoAttr = unwrapped2.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
                    var customName = nestedResponseDtoAttr?.NamedArguments
                        .FirstOrDefault(a => a.Key == "Name").Value.Value as string;
                    var rName = customName ?? $"{nestedName}Response";
                    var nestedNs = unwrapped2.ContainingNamespace.IsGlobalNamespace
                        ? null : unwrapped2.ContainingNamespace.ToDisplayString();
                    var fqResponseName = nestedNs is not null ? $"{nestedNs}.{rName}" : rName;
                    var displayType2 = isNullableRef2 ? $"{fqResponseName}?" : fqResponseName;
                    properties.Add(new ResponsePropertyInfo(prop.Name, jsonName, displayType2, validationAttrs,
                        isNestedResponse: true, isNullable: isNullableRef2,
                        sourceTypeName: propType2.ToDisplayString(), nestedResponseName: fqResponseName));
                }
            }
            // Check for [DtoIgnore(DtoTarget.List)] → separate list DTO
            var listIgnoreSet = new HashSet<string>();
            foreach (var prop in GetAllProperties(symbol))
            {
                var (ig, on) = GetDtoTargetFlags(prop);
                if (ig != 31 && ((ig & 16) != 0 || (on != 0 && (on & 16) == 0)))
                    listIgnoreSet.Add(prop.Name);
            }

            string? listName = null;
            List<ResponsePropertyInfo>? listProps = null;
            if (listIgnoreSet.Count > 0)
            {
                listName = $"{symbol.Name}ListItem";
                listProps = properties.Where(p => !listIgnoreSet.Contains(p.PropertyName)).ToList();
            }

            result.ResponseDtos.Add(new ResponseDtoInfo(symbol.Name, ns, fqn,
                $"{symbol.Name}Response", properties, listName, listProps));
        }

        if (needsQuery)
        {
            // Bridge: read [UiTable(DefaultSort)] if present
            string? tableDefaultSort = null;
            var tableAttr = allAttrs.FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == "ZibStack.NET.UI.UiTableAttribute");
            if (tableAttr is not null)
                tableDefaultSort = tableAttr.NamedArguments.FirstOrDefault(a => a.Key == "DefaultSort").Value.Value as string;

            var queryProps = new List<QueryPropertyInfo>();
            foreach (var prop in GetAllProperties(symbol))
            {
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetMethod is null) continue;
                var (cqIg, cqOn) = GetDtoTargetFlags(prop);
                // DtoTarget.Query = 8, All = 31
                if (cqIg == 31 || (cqIg & 8) != 0 || (cqOn != 0 && (cqOn & 8) == 0)) continue;

                // Bridge: [UiTableColumn(Filterable = false)] → skip from query
                // If [UiTableColumn] exists and Filterable is explicitly false, exclude
                var tableColAttr = prop.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == "ZibStack.NET.UI.UiTableColumnAttribute");
                if (tableColAttr is not null)
                {
                    var filterableArg = tableColAttr.NamedArguments.FirstOrDefault(a => a.Key == "Filterable");
                    if (filterableArg.Key is not null && filterableArg.Value.Value is false)
                        continue;
                }

                var propType = prop.Type;
                if (propType is INamedTypeSymbol nts2 && nts2.NullableAnnotation == NullableAnnotation.Annotated
                    && nts2.TypeArguments.Length == 1)
                    propType = nts2.TypeArguments[0];
                if (propType.TypeKind == TypeKind.Class && propType.SpecialType == SpecialType.None
                    && propType.ToDisplayString() != "string") continue;
                if (propType.TypeKind == TypeKind.Interface || propType.TypeKind == TypeKind.Array) continue;

                var jsonName = GetJsonName(prop);
                var displayType = prop.Type.ToDisplayString();
                var isValueType = prop.Type.IsValueType;
                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
                var nullableType = isNullable || !isValueType
                    ? displayType + (isNullable ? "" : "?")
                    : displayType + "?";
                queryProps.Add(new QueryPropertyInfo(prop.Name, jsonName, displayType, nullableType, isValueType));
            }
            // Collect navigation property paths for DSL filtering and select
            var navPaths = new List<QueryNavigationPath>();
            var navNames = new List<string>();
            var collectionPaths = new List<QueryCollectionPath>();
            foreach (var prop in GetAllProperties(symbol))
            {
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetMethod is null) continue;

                var pType = prop.Type;
                var hasOneToOne = prop.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.OneToOneAttribute");

                var isNav = hasOneToOne;
                if (!isNav && pType is INamedTypeSymbol navNts)
                {
                    var unwrapped = navNts.NullableAnnotation == NullableAnnotation.Annotated && navNts.TypeArguments.Length == 1
                        ? navNts.TypeArguments[0] as INamedTypeSymbol : navNts;
                    if (unwrapped is not null && unwrapped.TypeKind == TypeKind.Class
                        && unwrapped.SpecialType == SpecialType.None
                        && unwrapped.ToDisplayString() != "string"
                        && !unwrapped.AllInterfaces.Any(i => i.ToDisplayString().StartsWith("System.Collections")))
                    {
                        isNav = true;
                        pType = unwrapped;
                    }
                }
                // Check for OneToMany (ICollection<T>)
                var hasOneToMany = prop.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Core.OneToManyAttribute");
                if (hasOneToMany && pType is INamedTypeSymbol collNts)
                {
                    // Extract element type from ICollection<T>
                    var elementType = collNts.AllInterfaces
                        .Concat(new[] { collNts })
                        .Where(i => i.IsGenericType && i.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.ICollection") || i.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable"))
                        .SelectMany(i => i.TypeArguments)
                        .OfType<INamedTypeSymbol>()
                        .FirstOrDefault();
                    if (elementType is null && collNts.TypeArguments.Length == 1)
                        elementType = collNts.TypeArguments[0] as INamedTypeSymbol;

                    if (elementType is not null)
                    {
                        // Add Count path
                        collectionPaths.Add(new QueryCollectionPath(
                            $"{prop.Name.ToLowerInvariant()}.count", prop.Name, elementType.ToDisplayString(), "Count", "int", true));

                        // Add sub-property paths (Any/All)
                        foreach (var subProp in GetAllProperties(elementType))
                        {
                            if (subProp.DeclaredAccessibility != Accessibility.Public) continue;
                            if (subProp.GetMethod is null) continue;
                            var subType = subProp.Type;
                            var subIsValueType = subType.IsValueType;
                            if (subType is INamedTypeSymbol subNts2 && subNts2.NullableAnnotation == NullableAnnotation.Annotated && subNts2.TypeArguments.Length == 1)
                                subType = subNts2.TypeArguments[0];
                            if (subType.TypeKind == TypeKind.Class && subType.SpecialType == SpecialType.None && subType.ToDisplayString() != "string") continue;
                            if (subType.TypeKind == TypeKind.Interface || subType.TypeKind == TypeKind.Array) continue;

                            // tags.name = Any (default), tags.any.name = Any (explicit), tags.all.name = All
                            collectionPaths.Add(new QueryCollectionPath(
                                $"{prop.Name.ToLowerInvariant()}.{subProp.Name.ToLowerInvariant()}", prop.Name, elementType.ToDisplayString(), subProp.Name, subProp.Type.ToDisplayString(), subIsValueType));
                            collectionPaths.Add(new QueryCollectionPath(
                                $"{prop.Name.ToLowerInvariant()}.any.{subProp.Name.ToLowerInvariant()}", prop.Name, elementType.ToDisplayString(), subProp.Name, subProp.Type.ToDisplayString(), subIsValueType));
                            collectionPaths.Add(new QueryCollectionPath(
                                $"{prop.Name.ToLowerInvariant()}.all.{subProp.Name.ToLowerInvariant()}", prop.Name, elementType.ToDisplayString(), subProp.Name, subProp.Type.ToDisplayString(), subIsValueType));
                        }
                    }
                    continue;
                }

                if (!isNav) continue;
                navNames.Add(prop.Name);

                var navTypeSymbol = pType as INamedTypeSymbol;
                if (navTypeSymbol is null) continue;

                foreach (var subProp in GetAllProperties(navTypeSymbol))
                {
                    if (subProp.DeclaredAccessibility != Accessibility.Public) continue;
                    if (subProp.GetMethod is null) continue;

                    var subType = subProp.Type;
                    var subIsValueType = subType.IsValueType;
                    if (subType is INamedTypeSymbol subNts && subNts.NullableAnnotation == NullableAnnotation.Annotated
                        && subNts.TypeArguments.Length == 1)
                        subType = subNts.TypeArguments[0];
                    if (subType.TypeKind == TypeKind.Class && subType.SpecialType == SpecialType.None
                        && subType.ToDisplayString() != "string") continue;
                    if (subType.TypeKind == TypeKind.Interface || subType.TypeKind == TypeKind.Array) continue;

                    var dotPath = $"{prop.Name.ToLowerInvariant()}.{subProp.Name.ToLowerInvariant()}";
                    var exprPath = $"{prop.Name}.{subProp.Name}";
                    navPaths.Add(new QueryNavigationPath(dotPath, exprPath, subProp.Type.ToDisplayString(), subIsValueType));
                }
            }

            result.QueryDtos.Add(new QueryDtoInfo(symbol.Name, ns, fqn,
                $"{symbol.Name}Query", queryProps, sortable: true,
                defaultSort: tableDefaultSort, defaultSortDirection: 0,
                navigationPaths: navPaths, navigationNames: navNames, collectionPaths: collectionPaths));
        }

        return result;
    } catch { return null; } }

    private static string Pluralize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("z")
            || name.EndsWith("sh") || name.EndsWith("ch"))
            return name + "es";
        if (name.EndsWith("y") && name.Length > 1 && !IsVowel(name[name.Length - 2]))
            return name.Substring(0, name.Length - 1) + "ies";
        return name + "s";
    }

    private static bool IsVowel(char c)
        => "aeiouAEIOU".IndexOf(c) >= 0;
}
