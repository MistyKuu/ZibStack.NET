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

        var properties = CollectPropertiesFromType(targetType);

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var typeKeyword = syntax is RecordDeclarationSyntax ? "record" : "class";
        return new PartialFromInfo(
            symbol.Name,
            ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            targetType.ToDisplayString(),
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

        var targetTypes = new List<IntersectTargetInfo>();
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != IntersectFromAttributeFqn) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType) continue;

            var props = CollectPropertiesFromType(targetType);
            targetTypes.Add(new IntersectTargetInfo(targetType.ToDisplayString(), props));
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

        var propNames = new HashSet<string>();
        if (attr.ConstructorArguments.Length >= 2 && !attr.ConstructorArguments[1].IsNull)
        {
            foreach (var val in attr.ConstructorArguments[1].Values)
            {
                if (val.Value is string s)
                    propNames.Add(s);
            }
        }

        var allProps = CollectPropertiesFromType(targetType);
        var properties = new List<UtilsPropertyInfo>();
        foreach (var prop in allProps)
        {
            var include = isPick ? propNames.Contains(prop.PropertyName) : !propNames.Contains(prop.PropertyName);
            if (include)
                properties.Add(prop);
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        var typeKeyword = syntax is RecordDeclarationSyntax ? "record" : "class";
        return new PartialFromInfo(
            symbol.Name, ns,
            SanitizeHintName(symbol.ToDisplayString().Replace(".", "_")),
            targetType.ToDisplayString(), properties, typeKeyword);
    } catch { return null; } }

    private static List<UtilsPropertyInfo> CollectPropertiesFromType(INamedTypeSymbol type)
    {
        var properties = new List<UtilsPropertyInfo>();
        foreach (var prop in GetAllProperties(type))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.SetMethod is null || prop.GetMethod is null) continue;

            var displayType = prop.Type.ToDisplayString();
            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            var isValueType = prop.Type.IsValueType;

            var validationAttrs = GetValidationAttributes(prop);
            properties.Add(new UtilsPropertyInfo(
                prop.Name,
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
                args.Add(FormatTypedConstant(arg));
            foreach (var named in attr.NamedArguments)
                args.Add($"{named.Key} = {FormatTypedConstant(named.Value)}");

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
}
