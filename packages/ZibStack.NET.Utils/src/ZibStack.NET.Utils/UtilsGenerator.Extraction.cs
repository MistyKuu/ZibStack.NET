using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Utils;

public partial class UtilsGenerator
{
    // ─── Metadata extraction ───────────────────────────────────────────

    private static string SanitizeHintName(string name)
        => name.Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_').Replace('?', '_');

    private static PartialFromInfo? GetPartialFromInfo(GeneratorAttributeSyntaxContext context)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == PartialFromAttributeFqn);

        if (attr.ConstructorArguments.Length == 0) return null;
        var targetType = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
        if (targetType is null) return null;

        var targetFqn = targetType.ToDisplayString();
        var properties = CollectPropertiesFromType(targetType);

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var typeKeyword = syntax is RecordDeclarationSyntax ? "record" : "class";
        return new PartialFromInfo(
            symbol.Name,
            ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            targetFqn,
            properties,
            typeKeyword);
    } catch { return null; } }

    private static IntersectInfo? GetIntersectFromInfo(GeneratorAttributeSyntaxContext context)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        // Collect ALL [IntersectFrom] attributes on this class
        var targetTypes = new List<IntersectTargetInfo>();
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != IntersectFromAttributeFqn) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType) continue;

            var targetFqn = targetType.ToDisplayString();
            var props = CollectPropertiesFromType(targetType);
            targetTypes.Add(new IntersectTargetInfo(targetFqn, props));
        }

        if (targetTypes.Count == 0) return null;

        var typeKeyword = syntax is RecordDeclarationSyntax ? "record" : "class";
        return new IntersectInfo(
            symbol.Name,
            ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            targetTypes,
            typeKeyword);
    } catch { return null; } }

    private static PartialFromInfo? GetPickOmitInfo(GeneratorAttributeSyntaxContext context, string attributeFqn, bool isPick)
    { try {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeFqn);

        if (attr.ConstructorArguments.Length == 0) return null;
        var targetType = attr.ConstructorArguments[0].Value as INamedTypeSymbol;
        if (targetType is null) return null;

        // Second constructor arg is string[] of property names
        var propNames = new HashSet<string>();
        if (attr.ConstructorArguments.Length >= 2 && !attr.ConstructorArguments[1].IsNull)
        {
            foreach (var val in attr.ConstructorArguments[1].Values)
            {
                if (val.Value is string s)
                    propNames.Add(s);
            }
        }

        var targetFqn = targetType.ToDisplayString();
        var properties = new List<UtilsPropertyInfo>();

        foreach (var prop in GetAllProperties(targetType))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.SetMethod is null || prop.GetMethod is null) continue;

            var include = isPick ? propNames.Contains(prop.Name) : !propNames.Contains(prop.Name);
            if (!include) continue;

            var jsonName = GetJsonName(prop);
            var displayType = prop.Type.ToDisplayString();
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            var isValueType = prop.Type.IsValueType;
            var validationAttrs = GetValidationAttributes(prop);

            properties.Add(new UtilsPropertyInfo(
                prop.Name, jsonName, displayType, isNullable,
                isValueType, validationAttributes: validationAttrs));
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var typeKeyword = syntax is RecordDeclarationSyntax ? "record" : "class";
        return new PartialFromInfo(
            symbol.Name, ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            targetFqn, properties, typeKeyword);
    } catch { return null; } }

    // ─── Shared helpers ────────────────────────────────────────────────

    private static List<UtilsPropertyInfo> CollectPropertiesFromType(INamedTypeSymbol type)
    {
        var properties = new List<UtilsPropertyInfo>();
        foreach (var prop in GetAllProperties(type))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.SetMethod is null || prop.GetMethod is null) continue;

            var jsonName = GetJsonName(prop);
            var displayType = prop.Type.ToDisplayString();
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            var isValueType = prop.Type.IsValueType;

            var validationAttrs = GetValidationAttributes(prop);
            properties.Add(new UtilsPropertyInfo(
                prop.Name,
                jsonName,
                displayType,
                isNullable,
                isValueType,
                validationAttributes: validationAttrs));
        }
        return properties;
    }

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

    private static readonly HashSet<string> ValidationNamespaces = new()
    {
        "System.ComponentModel.DataAnnotations",
        "ZibStack.NET.Validation"
    };

    private static List<string> GetValidationAttributes(IPropertySymbol prop)
    {
        var attrs = new List<string>();

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
        }
        return attrs;
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

    private static string GetJsonName(IPropertySymbol prop)
    {
        var jsonPropAttr = prop.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonPropertyNameAttribute");
        if (jsonPropAttr is not null && jsonPropAttr.ConstructorArguments.Length > 0)
            return jsonPropAttr.ConstructorArguments[0].Value?.ToString() ?? CamelCase(prop.Name);

        return CamelCase(prop.Name);
    }

    private static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
