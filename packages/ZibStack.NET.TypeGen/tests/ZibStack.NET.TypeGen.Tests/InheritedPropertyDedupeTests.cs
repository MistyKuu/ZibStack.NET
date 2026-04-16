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
