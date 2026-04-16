using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Core;

public partial class CoreGenerator
{
    // Metadata name for the generic attribute — "`1" is the arity suffix.
    private const string DestructurableAttributeMetadataName = "ZibStack.NET.Core.DestructurableAttribute`1";

    private static readonly DiagnosticDescriptor DesctructurablePropMissing = new(
        id: "ZDS0001",
        title: "Destructurable shape property not found on source",
        messageFormat: "Property '{0}' on shape '{1}' does not exist on source type '{2}' — cannot destructure",
        category: "ZibStack.Destructurable",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DesctructurablePropTypeMismatch = new(
        id: "ZDS0002",
        title: "Destructurable shape property type mismatch",
        messageFormat: "Property '{0}' on shape '{1}' has type '{2}' but source '{3}' declares type '{4}'",
        category: "ZibStack.Destructurable",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DesctructurableNotPartial = new(
        id: "ZDS0003",
        title: "Destructurable shape must be partial",
        messageFormat: "Shape '{0}' carries [Destructurable<>] but is not declared 'partial' — generator cannot extend it",
        category: "ZibStack.Destructurable",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private sealed class DestructurableShapeInfo
    {
        public INamedTypeSymbol ShapeSymbol { get; }
        public TypeDeclarationSyntax ShapeSyntax { get; }
        public INamedTypeSymbol SourceSymbol { get; }
        public ImmutableArray<IPropertySymbol> ShapeProperties { get; }
        public ImmutableArray<IPropertySymbol> SourceProperties { get; }
        public bool ShapeHasPrimaryCtor { get; }

        public DestructurableShapeInfo(
            INamedTypeSymbol shape,
            TypeDeclarationSyntax shapeSyntax,
            INamedTypeSymbol source,
            ImmutableArray<IPropertySymbol> shapeProps,
            ImmutableArray<IPropertySymbol> sourceProps,
            bool hasPrimaryCtor)
        {
            ShapeSymbol = shape;
            ShapeSyntax = shapeSyntax;
            SourceSymbol = source;
            ShapeProperties = shapeProps;
            SourceProperties = sourceProps;
            ShapeHasPrimaryCtor = hasPrimaryCtor;
        }
    }

    private static void RegisterDestructurable(IncrementalGeneratorInitializationContext context)
    {
        var shapes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DestructurableAttributeMetadataName,
                predicate: static (node, _) => node is RecordDeclarationSyntax or ClassDeclarationSyntax,
                transform: static (ctx, _) => ExtractShapeInfo(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        context.RegisterSourceOutput(shapes, (spc, info) =>
        {
            EmitDestructurable(spc, info);
        });
    }

    private static DestructurableShapeInfo? ExtractShapeInfo(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol shape) return null;
        if (ctx.TargetNode is not TypeDeclarationSyntax shapeSyntax) return null;

        var attr = ctx.Attributes.FirstOrDefault();
        if (attr?.AttributeClass is not { TypeArguments.Length: 1 } attrClass) return null;
        if (attrClass.TypeArguments[0] is not INamedTypeSymbol source) return null;

        var shapeProps = shape.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public && p.GetMethod is not null)
            .ToImmutableArray();

        var sourceProps = GetAllPropertiesForSource(source);

        bool hasPrimaryCtor = shapeSyntax is RecordDeclarationSyntax rds && rds.ParameterList is { Parameters.Count: > 0 };

        return new DestructurableShapeInfo(shape, shapeSyntax, source, shapeProps, sourceProps, hasPrimaryCtor);
    }

    private static ImmutableArray<IPropertySymbol> GetAllPropertiesForSource(INamedTypeSymbol source)
    {
        var seen = new HashSet<string>();
        var props = new List<IPropertySymbol>();
        var cur = source;
        while (cur is not null && cur.SpecialType != SpecialType.System_Object)
        {
            foreach (var m in cur.GetMembers())
            {
                if (m is IPropertySymbol p
                    && !p.IsStatic && !p.IsIndexer
                    && p.DeclaredAccessibility == Accessibility.Public
                    && p.GetMethod is not null
                    && seen.Add(p.Name))
                {
                    props.Add(p);
                }
            }
            cur = cur.BaseType;
        }
        return props.ToImmutableArray();
    }

    private static void EmitDestructurable(SourceProductionContext spc, DestructurableShapeInfo info)
    {
        if (!info.ShapeSyntax.Modifiers.Any(m => m.ValueText == "partial"))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DesctructurableNotPartial,
                info.ShapeSyntax.Identifier.GetLocation(),
                info.ShapeSymbol.Name));
            return;
        }

        var sourcePropsByName = info.SourceProperties.ToDictionary(p => p.Name);
        var shapePropNames = new HashSet<string>();
        var matched = new List<(IPropertySymbol Shape, IPropertySymbol Source)>();
        bool hasError = false;

        foreach (var sp in info.ShapeProperties)
        {
            shapePropNames.Add(sp.Name);
            if (!sourcePropsByName.TryGetValue(sp.Name, out var srcProp))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DesctructurablePropMissing,
                    sp.Locations.FirstOrDefault() ?? info.ShapeSyntax.Identifier.GetLocation(),
                    sp.Name, info.ShapeSymbol.Name, info.SourceSymbol.Name));
                hasError = true;
                continue;
            }

            var shapeTypeFqn = sp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var srcTypeFqn = srcProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (shapeTypeFqn != srcTypeFqn)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DesctructurablePropTypeMismatch,
                    sp.Locations.FirstOrDefault() ?? info.ShapeSyntax.Identifier.GetLocation(),
                    sp.Name, info.ShapeSymbol.Name, sp.Type.ToDisplayString(),
                    info.SourceSymbol.Name, srcProp.Type.ToDisplayString()));
                hasError = true;
                continue;
            }
            matched.Add((sp, srcProp));
        }

        if (hasError) return;

        var restProps = info.SourceProperties
            .Where(p => !shapePropNames.Contains(p.Name))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var ns = info.ShapeSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : info.ShapeSymbol.ContainingNamespace.ToDisplayString();
        if (ns is not null)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";");
            sb.AppendLine();
        }

        var shapeKeyword = info.ShapeSyntax is RecordDeclarationSyntax r
            ? (r.ClassOrStructKeyword.ValueText == "struct" ? "record struct" : "record")
            : "class";
        var shapeAccessibility = FormatAccessibility(info.ShapeSymbol.DeclaredAccessibility);
        var srcFqn = info.SourceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var shapeName = info.ShapeSymbol.Name;

        sb.Append(shapeAccessibility).Append(" partial ").Append(shapeKeyword).Append(' ').Append(shapeName).AppendLine();
        sb.AppendLine("{");

        // ── nested Rest record (positional) ─────────────────────────────────
        sb.Append("    /// <summary>Complement of <see cref=\"").Append(shapeName).AppendLine("\"/> against its source type.</summary>");
        sb.Append("    public sealed record Rest(");
        for (int i = 0; i < restProps.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(restProps[i].Type.ToDisplayString()).Append(' ').Append(restProps[i].Name);
        }
        sb.AppendLine(");");
        sb.AppendLine();

        // ── FromSource(src) — returns picked shape ─────────────────────────
        sb.Append("    /// <summary>Projects <paramref name=\"src\"/> into the picked shape.</summary>");
        sb.AppendLine();
        sb.Append("    public static ").Append(shapeName).Append(" FromSource(").Append(srcFqn).Append(" src)");
        sb.AppendLine();
        if (info.ShapeHasPrimaryCtor)
        {
            sb.Append("        => new(");
            for (int i = 0; i < matched.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("src.").Append(matched[i].Source.Name);
            }
            sb.AppendLine(");");
        }
        else
        {
            sb.AppendLine("        => new()");
            sb.AppendLine("        {");
            foreach (var (shapeProp, srcProp) in matched)
                sb.Append("            ").Append(shapeProp.Name).Append(" = src.").Append(srcProp.Name).AppendLine(",");
            sb.AppendLine("        };");
        }
        sb.AppendLine();

        // ── RestOf(src) — returns complement ────────────────────────────────
        sb.Append("    /// <summary>Extracts the complement of the picked shape from <paramref name=\"src\"/>.</summary>");
        sb.AppendLine();
        sb.Append("    public static Rest RestOf(").Append(srcFqn).AppendLine(" src)");
        sb.Append("        => new(");
        for (int i = 0; i < restProps.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("src.").Append(restProps[i].Name);
        }
        sb.AppendLine(");");
        sb.AppendLine();

        // ── Split(src) — returns (picked, rest) ─────────────────────────────
        sb.Append("    /// <summary>JS-style <c>{ picked, ...rest }</c> — returns the shape and its complement.</summary>");
        sb.AppendLine();
        sb.Append("    public static (").Append(shapeName).Append(" Picked, Rest Remaining) Split(").Append(srcFqn).AppendLine(" src)");
        sb.AppendLine("        => (FromSource(src), RestOf(src));");

        sb.AppendLine("}");

        var hintName = (ns is null ? shapeName : $"{ns}.{shapeName}").Replace('.', '_');
        spc.AddSource($"{hintName}.Destructurable.g.cs", sb.ToString());
    }

    private static string FormatAccessibility(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal",
    };
}
