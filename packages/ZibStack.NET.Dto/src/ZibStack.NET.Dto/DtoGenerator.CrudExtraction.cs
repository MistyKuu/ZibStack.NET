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
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudApiAttributeFqn);
        if (attr is null) return null;

        var route = attr.NamedArguments.FirstOrDefault(a => a.Key == "Route").Value.Value as string;
        var keyProperty = attr.NamedArguments.FirstOrDefault(a => a.Key == "KeyProperty").Value.Value as string ?? "Id";
        var operationsRaw = attr.NamedArguments.FirstOrDefault(a => a.Key == "Operations").Value.Value;
        var operations = operationsRaw is int ops ? ops : 31; // CrudOperations.All = 31
        var styleRaw = attr.NamedArguments.FirstOrDefault(a => a.Key == "Style").Value.Value;
        var style = styleRaw is int s ? s : 0; // ApiStyle.MinimalApi = 0
        var authorizePolicy = attr.NamedArguments.FirstOrDefault(a => a.Key == "AuthorizePolicy").Value.Value as string;

        // Resolve key property type
        var keyProp = GetAllProperties(symbol).FirstOrDefault(p => p.Name == keyProperty);
        if (keyProp is null) return null;
        var keyTypeName = keyProp.Type.ToDisplayString();

        // Auto-generate route from class name if not specified
        if (route is null)
        {
            var name = symbol.Name;
            route = "api/" + Pluralize(CamelCase(name));
        }

        // Cross-reference DTO attributes on the same entity
        var allAttrs = symbol.GetAttributes();

        var hasCreateDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateDtoAttributeFqn);
        var hasUpdateDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
        var hasCombined = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == CreateOrUpdateDtoAttributeFqn);
        var hasResponseDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == ResponseDtoAttributeFqn);
        var hasQueryDto = allAttrs.Any(a => a.AttributeClass?.ToDisplayString() == QueryDtoAttributeFqn);

        // Resolve DTO type names
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

        if (hasUpdateDto)
        {
            var uAttr = allAttrs.First(a => a.AttributeClass?.ToDisplayString() == UpdateDtoAttributeFqn);
            var customName = uAttr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            updateRequestName = customName ?? $"Update{symbol.Name}Request";
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

        if (hasQueryDto)
        {
            var qAttr = allAttrs.First(a => a.AttributeClass?.ToDisplayString() == QueryDtoAttributeFqn);
            var customName = qAttr.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
            queryName = customName ?? $"{symbol.Name}Query";
        }

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
            hasQueryDto);
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
