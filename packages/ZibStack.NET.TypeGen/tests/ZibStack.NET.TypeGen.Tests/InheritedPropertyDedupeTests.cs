using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// When a class inherits from a non-emitted base whose own ancestor declares an
/// abstract property that the immediate base overrides, the parser used to add
/// the property TWICE — once from the base (override) and once from the
/// grandparent (abstract). The fix de-dupes by property name, with the most-
/// derived occurrence winning.
/// </summary>
public class InheritedPropertyDedupeTests
{
    private const string SourceUnderTest = """
        using ZibStack.NET.TypeGen;

        namespace InheritFix;

        public enum SolutionElementType { A, B }

        // Grandparent: abstract property — declares the contract.
        public abstract class C<T>
        {
            public abstract SolutionElementType Type { get; }
        }

        // Immediate parent: provides the concrete override.
        public class B : C<int>
        {
            public override SolutionElementType Type => SolutionElementType.B;
            public string Name { get; set; } = "";
        }

        // Subject under test — inherits Type via B (already concrete).
        // [GenerateTypes] only on D so emitter sees D as standalone (B is non-emitted →
        // its props get inlined into D, including the inheritance walk past B → C<int>
        // which is where the duplicate used to creep in).
        [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
        public class D : B
        {
            public int Extra { get; set; }
        }
        """;

    [Fact]
    public void InheritedAbstractProperty_NotDuplicatedWhenBaseOverrides()
    {
        var symbol = ParseClass("InheritFix.D");
        var cls = SchemaParser.ParseClass(symbol);
        Assert.NotNull(cls);

        // The `Type` property must appear EXACTLY ONCE — base override wins,
        // grandparent abstract is skipped by the dedupe walk.
        var typeOccurrences = cls!.Properties.Count(p => p.SourceName == "Type");
        Assert.Equal(1, typeOccurrences);

        // Sanity: other properties from each level still flow through.
        Assert.Contains(cls.Properties, p => p.SourceName == "Extra");   // declared on D
        Assert.Contains(cls.Properties, p => p.SourceName == "Name");    // inherited from B
        Assert.Contains(cls.Properties, p => p.SourceName == "Type");    // inherited from B (overrides C's abstract)
    }

    [Fact]
    public void InheritedPropertyEnumType_AutoDiscovered()
    {
        // Regression: transitive discovery used to walk `clsSymbol.GetMembers()`,
        // which returns DECLARED members only — inherited ones (copied into
        // cls.Properties by inlineInherited) were invisible, so enum / class types
        // referenced ONLY through inherited properties fell back to `unknown`.
        var symbol = ParseClass("InheritFix.D");
        var cls = SchemaParser.ParseClass(symbol);
        Assert.NotNull(cls);
        var model = new SchemaModel();
        model.Classes.Add(cls!);

        // Drive the full parser pipeline — DiscoverTransitive must surface
        // SolutionElementType even though no class IN the model (D) declares a
        // property of that type directly; the only reference is via the
        // inherited `Type` from B.
        var compilation = GetCompilation();
        SchemaParser.DiscoverTransitive(model, compilation);

        Assert.Contains(model.Enums, e => e.SourceName == "SolutionElementType");
    }

    [Fact]
    public void MultiLevelInheritance_AllProperties_Flatten()
    {
        // A → B → C → D chain, none of the intermediates annotated. D's emitted
        // schema must include every property up the chain, inlined once each.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Multi;

            public class Lvl0 { public int Id { get; set; } }
            public class Lvl1 : Lvl0 { public string Name { get; set; } = ""; }
            public class Lvl2 : Lvl1 { public string Email { get; set; } = ""; }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class D : Lvl2 { public int Extra { get; set; } }
            """;
        var compilation = MakeCompilation(src);
        var cls = SchemaParser.ParseClass(compilation.GetTypeByMetadataName("Multi.D")!);
        Assert.NotNull(cls);
        var names = cls!.Properties.Select(p => p.SourceName).ToList();
        Assert.Contains("Id", names);
        Assert.Contains("Name", names);
        Assert.Contains("Email", names);
        Assert.Contains("Extra", names);
        // No duplicates — each name appears exactly once.
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void MultiLevelInheritance_WithGenerateTypesOnAncestor_NoDuplication()
    {
        // A[GenerateTypes] → B → C → D[GenerateTypes]. A's props must appear on
        // A's own emitted schema but NOT be inlined into D — D should point at A
        // via the inheritance chain rather than duplicating A's members.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Multi;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class A { public int Id { get; set; } }

            public class B : A { public string Middle { get; set; } = ""; }
            public class C : B { public string Inner { get; set; } = ""; }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class D : C { public int Leaf { get; set; } }
            """;
        var compilation = MakeCompilation(src);
        var d = SchemaParser.ParseClass(compilation.GetTypeByMetadataName("Multi.D")!);
        Assert.NotNull(d);
        var names = d!.Properties.Select(p => p.SourceName).ToList();
        // D owns Leaf, inherits Middle + Inner from non-annotated bases (flattened).
        // A's Id must NOT appear — A is a separately-emitted [GenerateTypes] class
        // and D should express the relationship via extends / $ref.
        Assert.Contains("Leaf", names);
        Assert.Contains("Middle", names);
        Assert.Contains("Inner", names);
        Assert.DoesNotContain("Id", names);
    }

    private static CSharpCompilation MakeCompilation(string src)
    {
        const string stubs = """
            namespace ZibStack.NET.TypeGen
            {
                [System.Flags] public enum TypeTarget { None = 0, TypeScript = 1, OpenApi = 2, Python = 4 }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Enum)]
                public sealed class GenerateTypesAttribute : System.Attribute {
                    public TypeTarget Targets { get; set; }
                    public string? OutputDir { get; set; }
                }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        return CSharpCompilation.Create(
            "MultiInheritTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(src) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    [Fact]
    public void InheritedAbstractProperty_TypeScriptEmitsTypeOnce()
    {
        var symbol = ParseClass("InheritFix.D");
        var cls = SchemaParser.ParseClass(symbol);
        Assert.NotNull(cls);

        var model = new SchemaModel();
        model.Classes.Add(cls!);
        var ts = TypeScriptEmitter.Emit(model, new GlobalSettings()).First(f => f.FileName == "D.ts").Content;

        // The property name appears exactly once on the body of the interface,
        // regardless of property-name style applied. Match by case-insensitive
        // "type" prefix + colon to cover camelCase / PascalCase / snake_case.
        var typeLines = ts.Split('\n').Count(l =>
        {
            var t = l.TrimStart();
            return (t.StartsWith("type:", System.StringComparison.OrdinalIgnoreCase)
                 || t.StartsWith("type ", System.StringComparison.OrdinalIgnoreCase)
                 || t.StartsWith("Type:", System.StringComparison.Ordinal));
        });
        Assert.True(typeLines == 1,
            $"Expected exactly 1 'type' property line, got {typeLines}.\nFull TS output:\n{ts}");
    }

    private static INamedTypeSymbol ParseClass(string fullyQualifiedName)
    {
        var symbol = GetCompilation().GetTypeByMetadataName(fullyQualifiedName);
        Assert.NotNull(symbol);
        return symbol!;
    }

    private static CSharpCompilation GetCompilation()
    {
        // Stub the [GenerateTypes] attribute + TypeTarget enum so the parser sees
        // a real attribute class with the expected metadata name.
        const string stubs = """
            namespace ZibStack.NET.TypeGen
            {
                [System.Flags] public enum TypeTarget { None = 0, TypeScript = 1, OpenApi = 2, Python = 4 }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Enum)]
                public sealed class GenerateTypesAttribute : System.Attribute {
                    public TypeTarget Targets { get; set; }
                    public string? OutputDir { get; set; }
                }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        return CSharpCompilation.Create(
            "InheritDedupeTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(SourceUnderTest) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
