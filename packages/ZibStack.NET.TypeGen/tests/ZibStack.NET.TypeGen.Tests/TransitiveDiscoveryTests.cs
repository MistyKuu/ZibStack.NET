using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// When a <c>[GenerateTypes]</c> class references a nested object, the generator
/// walks the property graph and emits every reachable user-defined type. Before
/// this, nested types without their own <c>[GenerateTypes]</c> fell through to
/// <c>unknown</c> in TS / <c>type: object</c> in OpenAPI, producing unusable
/// output. The discovered nested types inherit the root's <c>Targets</c> and
/// <c>OutputDir</c>.
/// </summary>
public class TransitiveDiscoveryTests
{
    [Fact]
    public void NestedObject_IsAutoDiscovered_AndEmitted()
    {
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Order
            {
                public string Id { get; set; } = "";
                public Foo Thing { get; set; } = new();
            }

            // Foo has NO [GenerateTypes] — must still be emitted because Order references it.
            public class Foo
            {
                public string Name { get; set; } = "";
            }
            """);

        // Order was the explicit root, Foo was transitively discovered.
        Assert.Contains(model.Classes, c => c.SourceName == "Order");
        Assert.Contains(model.Classes, c => c.SourceName == "Foo");

        // Foo inherits Order's Targets + OutputDir.
        var foo = model.Classes.Single(c => c.SourceName == "Foo");
        Assert.Equal(TypeTarget.TypeScript, foo.Targets);
        Assert.Equal(".", foo.OutputDir);
    }

    [Fact]
    public void NestedObject_TypeScript_OrderImportsFoo_FooTsEmitted()
    {
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Order
            {
                public Foo Thing { get; set; } = new();
            }

            public class Foo
            {
                public int Qty { get; set; }
            }
            """);

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings()).ToList();

        var orderTs = files.First(f => f.FileName == "Order.ts").Content;
        Assert.Contains("import { Foo } from './Foo';", orderTs);
        Assert.Contains("thing: Foo;", orderTs);

        // Foo.ts also emitted (auto-discovered = implicitly part of emission).
        var fooTs = files.First(f => f.FileName == "Foo.ts").Content;
        Assert.Contains("export interface Foo", fooTs);
        Assert.Contains("qty: number;", fooTs);
    }

    [Fact]
    public void NestedObject_RecursiveWalk_TransitiveChain()
    {
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class A { public B Next { get; set; } = new(); }
            public class B { public C Next { get; set; } = new(); }
            public class C { public string Leaf { get; set; } = ""; }
            """);

        Assert.Contains(model.Classes, c => c.SourceName == "A");
        Assert.Contains(model.Classes, c => c.SourceName == "B");
        Assert.Contains(model.Classes, c => c.SourceName == "C");
    }

    [Fact]
    public void NestedObject_InListGenericDictionary_StillDiscovered()
    {
        var model = ParseWithDiscovery("""
            using System.Collections.Generic;
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Root
            {
                public List<Item> Items { get; set; } = new();
                public Dictionary<string, Meta> Metas { get; set; } = new();
                public Child[] Kids { get; set; } = System.Array.Empty<Child>();
            }

            public class Item { public string Name { get; set; } = ""; }
            public class Meta { public int Score { get; set; } }
            public class Child { public bool Active { get; set; } }
            """);

        Assert.Contains(model.Classes, c => c.SourceName == "Item");
        Assert.Contains(model.Classes, c => c.SourceName == "Meta");
        Assert.Contains(model.Classes, c => c.SourceName == "Child");
    }

    [Fact]
    public void NestedObject_Cycle_DoesNotInfiniteLoop()
    {
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Node
            {
                public Node? Parent { get; set; }
                public Node? Left { get; set; }
                public Node? Right { get; set; }
            }
            """);

        // Root is added; cycle via Parent/Left/Right must be detected by the
        // "already in model" check — single entry, no duplicates.
        Assert.Equal(1, model.Classes.Count(c => c.SourceName == "Node"));
    }

    [Fact]
    public void NestedObject_BCLTypes_Skipped()
    {
        var model = ParseWithDiscovery("""
            using System;
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Plain
            {
                public string S { get; set; } = "";
                public Guid G { get; set; }
                public DateTime D { get; set; }
                public int I { get; set; }
            }
            """);

        // Only the root — BCL types (string, Guid, DateTime, int) must not be pulled in.
        Assert.Single(model.Classes);
    }

    [Fact]
    public void NestedObject_EnumProperty_IsAutoDiscovered()
    {
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Order
            {
                public OrderStatus Status { get; set; }
                public Priority Priority { get; set; }
            }

            public enum OrderStatus { Pending, Shipped, Delivered }
            public enum Priority { Low, Normal, High }
            """);

        // Both enums land in model.Enums with Order's Targets + OutputDir.
        Assert.Contains(model.Enums, e => e.SourceName == "OrderStatus");
        Assert.Contains(model.Enums, e => e.SourceName == "Priority");
        var status = model.Enums.Single(e => e.SourceName == "OrderStatus");
        Assert.Equal(TypeTarget.TypeScript, status.Targets);
        Assert.Equal(".", status.OutputDir);
    }

    [Fact]
    public void NestedObject_EnumInsideNestedClass_FoundByRecursiveWalk()
    {
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Root { public Inner Inner { get; set; } = new(); }
            public class Inner { public Kind K { get; set; } }
            public enum Kind { A, B }
            """);

        // Inner is a class, Kind is an enum — both auto-discovered transitively.
        Assert.Contains(model.Classes, c => c.SourceName == "Inner");
        Assert.Contains(model.Enums, e => e.SourceName == "Kind");
    }

    [Fact]
    public void NestedObject_WithTsNameAttribute_RespectsOverride()
    {
        // [TsName] on an auto-discovered nested class still takes effect because
        // ParseAuxiliaryClass reads the class attributes the same way ParseClass does.
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Root { public Item Thing { get; set; } = new(); }

            [TsName("RenamedItem")]
            public class Item { public string X { get; set; } = ""; }
            """);

        var item = model.Classes.Single(c => c.SourceName == "Item");
        Assert.Equal("RenamedItem", item.TsNameOverride);
    }

    [Fact]
    public void NestedObject_WithTsIgnoreAttribute_StillDiscovered_ButMarkedIgnored()
    {
        // [TsIgnore] flows into the discovered SchemaClass; the emitter decides
        // whether to skip it. We only assert parser behavior here.
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Root { public Hidden H { get; set; } = new(); }

            [TsIgnore]
            public class Hidden { public int X { get; set; } }
            """);

        var hidden = model.Classes.Single(c => c.SourceName == "Hidden");
        Assert.True(hidden.TsIgnore);
    }

    [Fact]
    public void NestedObject_ExplicitGenerateTypesOnChild_IsRespected()
    {
        // When the child already has [GenerateTypes] with different config, the
        // explicit settings win — auto-discovery must not overwrite.
        var model = ParseWithDiscovery("""
            using ZibStack.NET.TypeGen;
            namespace Nested;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = "./a")]
            public class P { public C Thing { get; set; } = new(); }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = "./b")]
            public class C { public string X { get; set; } = ""; }
            """);

        var c = model.Classes.Single(x => x.SourceName == "C");
        Assert.Equal("./b", c.OutputDir);   // explicit, NOT "./a"
    }

    // ── Roslyn harness ──────────────────────────────────────────────────────

    private static SchemaModel ParseWithDiscovery(string src)
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
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct
                    | System.AttributeTargets.Property | System.AttributeTargets.Field | System.AttributeTargets.Enum)]
                public sealed class TsNameAttribute : System.Attribute {
                    public string Name { get; }
                    public TsNameAttribute(string name) => Name = name;
                }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct
                    | System.AttributeTargets.Property | System.AttributeTargets.Field | System.AttributeTargets.Enum)]
                public sealed class TsIgnoreAttribute : System.Attribute { }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "TransitiveDiscoveryTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(src) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var model = new SchemaModel();
        // Seed with [GenerateTypes]-annotated roots (mirrors the generator's main pipeline).
        var roots = new List<INamedTypeSymbol>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var sm = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>())
            {
                if (sm.GetDeclaredSymbol(node) is INamedTypeSymbol s && SchemaParser.HasGenerateTypes(s))
                    roots.Add(s);
            }
        }
        foreach (var root in roots)
        {
            var cls = SchemaParser.ParseClass(root);
            if (cls is not null) model.Classes.Add(cls);
        }
        // Run the transitive closure pass on the seeded model.
        SchemaParser.DiscoverBaseClasses(model, compilation);
        SchemaParser.DiscoverTransitive(model, compilation);
        return model;
    }
}
