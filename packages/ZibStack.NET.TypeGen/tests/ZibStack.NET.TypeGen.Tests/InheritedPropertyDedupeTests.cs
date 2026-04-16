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
        // New semantics: inheritance structure is preserved, not flattened.
        //   D : B : C<int>  (generic bases now emittable too)
        // Each level gets its own schema — `Type` is declared ONCE, on C<T>
        // (where the abstract declaration lives); B's override is suppressed
        // because an emittable ancestor already carries it (extends covers it).
        var model = BuildModelWithBaseDiscovery();

        var d = model.Classes.Single(c => c.SourceName == "D");
        Assert.Equal(new[] { "Extra" }, d.Properties.Select(p => p.SourceName));
        Assert.EndsWith(".B", d.BaseClassFullName);

        var b = model.Classes.Single(c => c.SourceName == "B");
        // B's override of Type is covered by the extends C<int> chain — no
        // redeclaration on B. Only Name remains on B.
        Assert.Equal(0, b.Properties.Count(p => p.SourceName == "Type"));
        Assert.Contains(b.Properties, p => p.SourceName == "Name");

        var c = model.Classes.Single(cc => cc.SourceName == "C");
        Assert.Contains(c.Properties, p => p.SourceName == "Type");
    }

    private static SchemaModel BuildModelWithBaseDiscovery()
    {
        var compilation = GetCompilation();
        var d = SchemaParser.ParseClass(compilation.GetTypeByMetadataName("InheritFix.D")!)!;
        var model = new SchemaModel();
        model.Classes.Add(d);
        SchemaParser.DiscoverBaseClasses(model, compilation);
        return model;
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
    public void MultiLevelInheritance_EachLevelEmittedSeparately_StructurePreserved()
    {
        // A → B → C → D chain, only D annotated. The generator mirrors the C#
        // structure: each level gets its own schema owning its own declared
        // properties, connected via `extends` / `$ref`. Nothing gets flattened.
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
        var d = SchemaParser.ParseClass(compilation.GetTypeByMetadataName("Multi.D")!)!;
        var model = new SchemaModel();
        model.Classes.Add(d);
        SchemaParser.DiscoverBaseClasses(model, compilation);

        // Every level pulled into the model as its own schema.
        Assert.Contains(model.Classes, c => c.SourceName == "Lvl0");
        Assert.Contains(model.Classes, c => c.SourceName == "Lvl1");
        Assert.Contains(model.Classes, c => c.SourceName == "Lvl2");
        Assert.Contains(model.Classes, c => c.SourceName == "D");

        // Each schema owns only its declared properties.
        Assert.Equal(new[] { "Id" }, model.Classes.Single(c => c.SourceName == "Lvl0").Properties.Select(p => p.SourceName));
        Assert.Equal(new[] { "Name" }, model.Classes.Single(c => c.SourceName == "Lvl1").Properties.Select(p => p.SourceName));
        Assert.Equal(new[] { "Email" }, model.Classes.Single(c => c.SourceName == "Lvl2").Properties.Select(p => p.SourceName));
        Assert.Equal(new[] { "Extra" }, model.Classes.Single(c => c.SourceName == "D").Properties.Select(p => p.SourceName));

        // extends chain follows C# inheritance.
        Assert.EndsWith(".Lvl2", model.Classes.Single(c => c.SourceName == "D").BaseClassFullName);
        Assert.EndsWith(".Lvl1", model.Classes.Single(c => c.SourceName == "Lvl2").BaseClassFullName);
        Assert.EndsWith(".Lvl0", model.Classes.Single(c => c.SourceName == "Lvl1").BaseClassFullName);
        Assert.Null(model.Classes.Single(c => c.SourceName == "Lvl0").BaseClassFullName);
    }

    [Fact]
    public void GenericBase_EmittedAsStandaloneGenericInterface_NotFlattened()
    {
        // Base<T> records the open generic as its own schema; Derived : Base<SomeType>
        // references it via `extends Base<SomeType>`. Should NOT flatten — the
        // generic base's properties belong to Base, not duplicated into every
        // concrete derived.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace GenBase;

            public record Base<T>(T Payload);
            public record SomeType(string Val);

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public record Derived(int Extra, SomeType Body) : Base<SomeType>(Body);
            """;
        var compilation = MakeCompilation(src);
        var model = new SchemaModel();
        model.Classes.Add(SchemaParser.ParseClass(compilation.GetTypeByMetadataName("GenBase.Derived")!)!);
        SchemaParser.DiscoverBaseClasses(model, compilation);
        SchemaParser.DiscoverTransitive(model, compilation);

        // Base<T> got pulled in as its own schema.
        Assert.Contains(model.Classes, c => c.SourceName == "Base");
        var baseCls = model.Classes.Single(c => c.SourceName == "Base");
        // Payload (type T) lives ON Base, not inlined into Derived.
        Assert.Contains(baseCls.Properties, p => p.SourceName == "Payload");

        var derived = model.Classes.Single(c => c.SourceName == "Derived");
        var derivedProps = derived.Properties.Select(p => p.SourceName).ToList();
        Assert.Contains("Extra", derivedProps);
        Assert.DoesNotContain("Payload", derivedProps);   // belongs to Base, not Derived

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings()).ToList();
        var baseTs = files.First(f => f.FileName == "Base.ts").Content;
        Assert.Contains("export interface Base<T>", baseTs);
        Assert.Contains("payload: T;", baseTs);

        var derivedTs = files.First(f => f.FileName == "Derived.ts").Content;
        Assert.Contains("extends Base<SomeType>", derivedTs);
    }

    [Fact]
    public void AbstractBaseOverride_NotDuplicatedAcrossLevels()
    {
        // AbsBase (abstract, non-generic) → Concrete (override Shared) → Leaf[GenerateTypes].
        // All three are emittable as standalone schemas. `Shared` is declared
        // abstract on AbsBase, overridden on Concrete. Without special handling
        // Concrete.ts would emit Shared again (redundant with the inherited
        // declaration from AbsBase). Skip overrides whose counterpart already
        // exists on an emittable ancestor — extends covers them.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Multi;

            public abstract class AbsBase { public abstract int Shared { get; } }
            public class Concrete : AbsBase
            {
                public override int Shared => 0;
                public int Extra { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Leaf : Concrete { public int More { get; set; } }
            """;
        var compilation = MakeCompilation(src);
        var model = new SchemaModel();
        model.Classes.Add(SchemaParser.ParseClass(compilation.GetTypeByMetadataName("Multi.Leaf")!)!);
        SchemaParser.DiscoverBaseClasses(model, compilation);

        var abs = model.Classes.Single(c => c.SourceName == "AbsBase");
        var concrete = model.Classes.Single(c => c.SourceName == "Concrete");
        var leaf = model.Classes.Single(c => c.SourceName == "Leaf");

        Assert.Equal(new[] { "Shared" }, abs.Properties.Select(p => p.SourceName));
        // Concrete should NOT repeat Shared — it's inherited from AbsBase via extends.
        Assert.Equal(new[] { "Extra" }, concrete.Properties.Select(p => p.SourceName));
        Assert.Equal(new[] { "More" }, leaf.Properties.Select(p => p.SourceName));
    }

    [Fact]
    public void MultiLevelInheritance_WithGenerateTypesOnAncestor_NoDuplication()
    {
        // A[GenerateTypes] → B → C → D[GenerateTypes]. A and D are initial roots.
        // DiscoverBaseClasses pulls B, C as aux (same Targets/OutputDir as D).
        // Each class owns only its declared members; the full inheritance chain is
        // preserved in emitted extends/$ref.
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
        var model = new SchemaModel();
        model.Classes.Add(SchemaParser.ParseClass(compilation.GetTypeByMetadataName("Multi.A")!)!);
        model.Classes.Add(SchemaParser.ParseClass(compilation.GetTypeByMetadataName("Multi.D")!)!);
        SchemaParser.DiscoverBaseClasses(model, compilation);

        Assert.Equal(new[] { "Id" }, model.Classes.Single(c => c.SourceName == "A").Properties.Select(p => p.SourceName));
        Assert.Equal(new[] { "Middle" }, model.Classes.Single(c => c.SourceName == "B").Properties.Select(p => p.SourceName));
        Assert.Equal(new[] { "Inner" }, model.Classes.Single(c => c.SourceName == "C").Properties.Select(p => p.SourceName));
        Assert.Equal(new[] { "Leaf" }, model.Classes.Single(c => c.SourceName == "D").Properties.Select(p => p.SourceName));

        // D → C → B → A — each level points at its immediate C# base.
        Assert.EndsWith(".C", model.Classes.Single(c => c.SourceName == "D").BaseClassFullName);
        Assert.EndsWith(".B", model.Classes.Single(c => c.SourceName == "C").BaseClassFullName);
        Assert.EndsWith(".A", model.Classes.Single(c => c.SourceName == "B").BaseClassFullName);
        Assert.Null(model.Classes.Single(c => c.SourceName == "A").BaseClassFullName);
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
        // `Type` lives on B (its concrete override). D extends B, so D.ts itself
        // has no `Type` line. Across the full emitted output set (D.ts + B.ts)
        // the member appears exactly once — on B.
        var model = BuildModelWithBaseDiscovery();
        var files = TypeScriptEmitter.Emit(model, new GlobalSettings()).ToList();
        var combined = string.Join("\n", files.Select(f => f.Content));
        var typeLines = combined.Split('\n').Count(l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("type:", System.StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Type:", System.StringComparison.Ordinal);
        });
        Assert.True(typeLines == 1,
            $"Expected exactly 1 'type' property line across emitted files, got {typeLines}.\nCombined:\n{combined}");

        // Structural check — D extends B in TS.
        var dTs = files.First(f => f.FileName == "D.ts").Content;
        Assert.Contains("export interface D extends B", dTs);
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
