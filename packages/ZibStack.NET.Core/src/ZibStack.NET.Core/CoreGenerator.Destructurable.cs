using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Core;

public partial class CoreGenerator
{
    private const string DestructurableAttributeFqn = "ZibStack.NET.Core.DestructurableAttribute";

    // ── Models ───────────────────────────────────────────────────────────

    /// <summary>One [Destructurable] type with its public instance properties.</summary>
    private sealed class DestructurableTypeInfo
    {
        public string FullyQualifiedName { get; }
        public string Namespace { get; }
        public string TypeName { get; }
        public string TypeKeyword { get; } // "class" | "struct" | "record"
        public bool IsRecord { get; }
        public bool IsPartial { get; }
        public ImmutableArray<DestructProperty> Properties { get; }

        public DestructurableTypeInfo(string fqn, string ns, string typeName, string typeKeyword, bool isRecord, bool isPartial, ImmutableArray<DestructProperty> props)
        {
            FullyQualifiedName = fqn;
            Namespace = ns;
            TypeName = typeName;
            TypeKeyword = typeKeyword;
            IsRecord = isRecord;
            IsPartial = isPartial;
            Properties = props;
        }
    }

    private sealed class DestructProperty
    {
        public string Name { get; }
        public string TypeFqn { get; }

        public DestructProperty(string name, string typeFqn)
        {
            Name = name;
            TypeFqn = typeFqn;
        }
    }

    /// <summary>A specific PickXxx call site discovered in user code.</summary>
    private sealed class PickInvocation
    {
        public string ReceiverTypeFqn { get; }
        public string MethodName { get; } // e.g. "PickNameId"

        public PickInvocation(string receiverTypeFqn, string methodName)
        {
            ReceiverTypeFqn = receiverTypeFqn;
            MethodName = methodName;
        }

        public override int GetHashCode() => unchecked(ReceiverTypeFqn.GetHashCode() * 397 ^ MethodName.GetHashCode());
        public override bool Equals(object? obj) => obj is PickInvocation o && o.ReceiverTypeFqn == ReceiverTypeFqn && o.MethodName == MethodName;
    }

    // ── Pipeline ─────────────────────────────────────────────────────────

    private static void RegisterDestructurable(IncrementalGeneratorInitializationContext context)
    {
        // Step 1: collect all [Destructurable] types
        var destructurableTypes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DestructurableAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => GetDestructurableInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!)
            .Collect();

        // Step 2: scan all PickXxx() invocations across the compilation
        var pickInvocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidatePickInvocation(node),
                transform: static (ctx, _) => GetPickInvocation(ctx))
            .Where(static pi => pi is not null)
            .Select(static (pi, _) => pi!)
            .Collect();

        // Step 3: combine and emit per source type
        var combined = destructurableTypes.Combine(pickInvocations);
        context.RegisterSourceOutput(combined, (spc, pair) =>
        {
            var (types, invocations) = pair;
            if (types.IsDefaultOrEmpty || types.Length == 0) return;

            // Index types by FQN for lookup
            var byFqn = new Dictionary<string, DestructurableTypeInfo>();
            foreach (var t in types)
                byFqn[t.FullyQualifiedName] = t;

            // For each source type:
            // 1. Always emit a single-property PickX() for each property (baseline — gives
            //    instant feedback in IDE/playground without needing the user to write a call).
            // 2. Add any multi-property PickXxx() combos discovered at call sites.
            foreach (var typeInfo in types)
            {
                var combos = new HashSet<string>(); // dedupe by method name
                var resolved = new List<List<DestructProperty>>();

                // Baseline: one PickX() per property
                foreach (var prop in typeInfo.Properties)
                {
                    var methodName = "Pick" + prop.Name;
                    if (!combos.Add(methodName)) continue;
                    resolved.Add(new List<DestructProperty> { prop });
                }

                // User-requested combos from PickXxx() invocations
                foreach (var inv in invocations)
                {
                    if (inv.ReceiverTypeFqn != typeInfo.FullyQualifiedName) continue;
                    if (!combos.Add(inv.MethodName)) continue;

                    var picked = ResolvePickMethodName(inv.MethodName, typeInfo.Properties);
                    if (picked is null) continue;
                    resolved.Add(picked);
                }

                if (resolved.Count == 0) continue;

                var source = EmitDestructurable(typeInfo, resolved);
                var hintName = typeInfo.FullyQualifiedName.Replace("global::", "").Replace("::", ".");
                spc.AddSource($"{hintName}.Destructurable.g.cs", source);
            }
        });
    }

    // ── Symbol extraction ────────────────────────────────────────────────

    private static DestructurableTypeInfo? GetDestructurableInfo(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;

        var props = new List<DestructProperty>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.IsStatic || prop.IsIndexer) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.GetMethod is null) continue;
            props.Add(new DestructProperty(prop.Name, prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
        if (props.Count == 0) return null;

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace ? "" : typeSymbol.ContainingNamespace.ToDisplayString();
        var typeKeyword = typeSymbol.IsRecord ? (typeSymbol.IsValueType ? "record struct" : "record") : (typeSymbol.IsValueType ? "struct" : "class");

        // Check if any declaration uses 'partial' modifier
        bool isPartial = false;
        foreach (var declRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (declRef.GetSyntax() is TypeDeclarationSyntax tds &&
                tds.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                isPartial = true;
                break;
            }
        }

        return new DestructurableTypeInfo(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ns,
            typeSymbol.Name,
            typeKeyword,
            typeSymbol.IsRecord,
            isPartial,
            props.ToImmutableArray());
    }

    // ── Invocation scanning ──────────────────────────────────────────────

    private static bool IsCandidatePickInvocation(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.ArgumentList.Arguments.Count != 0) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax mem) return false;
        var name = mem.Name.Identifier.Text;
        if (name.Length < 5 || !name.StartsWith("Pick")) return false;
        // 5th char must be uppercase to distinguish from "pick" anything
        return char.IsUpper(name[4]);
    }

    private static PickInvocation? GetPickInvocation(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not InvocationExpressionSyntax inv) return null;
        if (inv.Expression is not MemberAccessExpressionSyntax mem) return null;
        var name = mem.Name.Identifier.Text;

        var receiverType = ctx.SemanticModel.GetTypeInfo(mem.Expression).Type;
        if (receiverType is null) return null;

        // Only consider receivers that are [Destructurable]
        bool hasAttr = false;
        foreach (var a in receiverType.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() == DestructurableAttributeFqn) { hasAttr = true; break; }
        }
        if (!hasAttr) return null;

        return new PickInvocation(receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), name);
    }

    // ── Name resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Greedy longest-match: parses "PickNameId" against the type's properties,
    /// preferring the longest property name first to disambiguate
    /// (e.g. "Name" vs "NameId" — if both exist, longest wins).
    /// </summary>
    private static List<DestructProperty>? ResolvePickMethodName(string methodName, ImmutableArray<DestructProperty> typeProps)
    {
        if (!methodName.StartsWith("Pick")) return null;
        var suffix = methodName.Substring(4);
        if (suffix.Length == 0) return null;

        var byLengthDesc = typeProps.OrderByDescending(p => p.Name.Length).ToList();
        var picked = new List<DestructProperty>();
        var pos = 0;

        while (pos < suffix.Length)
        {
            DestructProperty? matched = null;
            foreach (var p in byLengthDesc)
            {
                if (suffix.Length - pos >= p.Name.Length &&
                    string.CompareOrdinal(suffix, pos, p.Name, 0, p.Name.Length) == 0)
                {
                    // Avoid double-picking the same property in one method
                    if (picked.Any(x => x.Name == p.Name)) continue;
                    matched = p;
                    break;
                }
            }
            if (matched is null) return null;
            picked.Add(matched);
            pos += matched.Name.Length;
        }

        return picked.Count > 0 ? picked : null;
    }

    // ── Emission ─────────────────────────────────────────────────────────

    private static string EmitDestructurable(DestructurableTypeInfo type, List<List<DestructProperty>> combos)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(type.Namespace))
        {
            sb.Append("namespace ").Append(type.Namespace).AppendLine();
            sb.AppendLine("{");
        }

        var indent = string.IsNullOrEmpty(type.Namespace) ? "" : "    ";
        var extClassName = $"__{type.TypeName}_Destructurable";

        // ── Code map: partial type with XML summary listing all generated picks ──
        // Only emit when the source type is declared 'partial' so we can extend it.
        // Hovering the source type in the IDE then shows clickable links to all
        // generated PickXxx methods and rest types.
        if (type.IsPartial)
        {
            sb.Append(indent).AppendLine("/// <summary>");
            sb.Append(indent).Append("/// Destructurable code map for <see cref=\"").Append(type.TypeName).AppendLine("\"/>:");
            sb.Append(indent).AppendLine("/// <list type=\"bullet\">");
            sb.Append(indent).Append("/// <item><description>Pick methods: <see cref=\"").Append(extClassName).AppendLine("\"/></description></item>");
            foreach (var combo in combos)
            {
                var methodName = "Pick" + string.Concat(combo.Select(p => p.Name));
                var restName = MakeRestTypeName(type.TypeName, combo);
                var pickedDesc = string.Join(", ", combo.Select(p => p.Name));
                sb.Append(indent).Append("/// <item><description><see cref=\"")
                  .Append(extClassName).Append('.').Append(methodName).Append("(").Append(type.TypeName).Append(")\"/> — picks ")
                  .Append(pickedDesc).Append(", rest as <see cref=\"")
                  .Append(restName).AppendLine("\"/></description></item>");
            }
            sb.Append(indent).AppendLine("/// </list>");
            sb.Append(indent).AppendLine("/// </summary>");
            sb.Append(indent).Append("partial ").Append(type.TypeKeyword).Append(' ').Append(type.TypeName).AppendLine(" { }");
            sb.AppendLine();
        }

        // ── Extension class with PickXxx methods ──
        sb.Append(indent).AppendLine("/// <summary>");
        sb.Append(indent).Append("/// Auto-generated destructuring methods for <see cref=\"").Append(type.TypeName).AppendLine("\"/>.");
        sb.Append(indent).AppendLine("/// Each <c>PickXxx()</c> returns a tuple of the picked properties plus a 'rest' object.");
        sb.Append(indent).AppendLine("/// </summary>");
        sb.Append(indent).Append("internal static class ").Append(extClassName).AppendLine();
        sb.Append(indent).AppendLine("{");

        foreach (var combo in combos)
        {
            EmitOneCombo(sb, type, combo, indent + "    ");
            sb.AppendLine();
        }

        sb.Append(indent).AppendLine("}");

        // ── Rest types ──
        var restTypes = new HashSet<string>();
        foreach (var combo in combos)
        {
            var restName = MakeRestTypeName(type.TypeName, combo);
            if (!restTypes.Add(restName)) continue;
            EmitRestType(sb, type, combo, restName, indent);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(type.Namespace))
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitOneCombo(StringBuilder sb, DestructurableTypeInfo type, List<DestructProperty> picked, string indent)
    {
        var methodName = "Pick" + string.Concat(picked.Select(p => p.Name));
        var restName = MakeRestTypeName(type.TypeName, picked);
        var pickedDesc = string.Join(", ", picked.Select(p => p.Name));

        // Tuple return type: (T1 Name, T2 Id, RestType Rest)
        var tupleParts = new List<string>(picked.Count + 1);
        foreach (var p in picked)
            tupleParts.Add($"{p.TypeFqn} {p.Name}");
        tupleParts.Add($"global::{(string.IsNullOrEmpty(type.Namespace) ? "" : type.Namespace + ".")}{restName} RestPart");
        var tupleType = "(" + string.Join(", ", tupleParts) + ")";

        sb.Append(indent).AppendLine("/// <summary>");
        sb.Append(indent).Append("/// Picks <c>").Append(pickedDesc).Append("</c> from <see cref=\"").Append(type.TypeName).AppendLine("\"/>.");
        sb.Append(indent).Append("/// The remaining properties are returned as <see cref=\"").Append(restName).AppendLine("\"/>.");
        sb.Append(indent).AppendLine("/// </summary>");
        sb.Append(indent).Append("public static ").Append(tupleType).Append(' ').Append(methodName).Append("(this ").Append(type.FullyQualifiedName).AppendLine(" __src)");
        sb.Append(indent).AppendLine("{");

        var pickedNames = new HashSet<string>(picked.Select(p => p.Name));
        sb.Append(indent).Append("    var __rest = new ").Append(restName).AppendLine();
        sb.Append(indent).AppendLine("    {");
        foreach (var p in type.Properties)
        {
            if (pickedNames.Contains(p.Name)) continue;
            sb.Append(indent).Append("        ").Append(p.Name).Append(" = __src.").Append(p.Name).AppendLine(",");
        }
        sb.Append(indent).AppendLine("    };");

        var returnParts = new List<string>(picked.Count + 1);
        foreach (var p in picked) returnParts.Add($"__src.{p.Name}");
        returnParts.Add("__rest");
        sb.Append(indent).Append("    return (").Append(string.Join(", ", returnParts)).AppendLine(");");
        sb.Append(indent).AppendLine("}");
    }

    private static void EmitRestType(StringBuilder sb, DestructurableTypeInfo type, List<DestructProperty> picked, string restName, string indent)
    {
        var pickedNames = new HashSet<string>(picked.Select(p => p.Name));
        var pickedDesc = string.Join(", ", picked.Select(p => p.Name));

        sb.Append(indent).AppendLine("/// <summary>");
        sb.Append(indent).Append("/// Remaining properties of <see cref=\"").Append(type.TypeName).Append("\"/> after picking <c>").Append(pickedDesc).AppendLine("</c>.");
        sb.Append(indent).AppendLine("/// </summary>");
        sb.Append(indent).Append("public sealed class ").Append(restName).AppendLine();
        sb.Append(indent).AppendLine("{");
        foreach (var p in type.Properties)
        {
            if (pickedNames.Contains(p.Name)) continue;
            sb.Append(indent).Append("    public ").Append(p.TypeFqn).Append(' ').Append(p.Name).AppendLine(" { get; set; } = default!;");
        }
        sb.Append(indent).AppendLine("}");
    }

    private static string MakeRestTypeName(string typeName, List<DestructProperty> picked)
    {
        var sb = new StringBuilder();
        sb.Append(typeName).Append("Rest_");
        foreach (var p in picked) sb.Append(p.Name);
        return sb.ToString();
    }
}
