using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// <c>[JsonPolymorphic] + [JsonDerivedType]</c> on an abstract base tells
/// TypeGen to emit the base as a discriminated union:
/// <list type="bullet">
///   <item>TS: <c>type Shape = Circle | Square</c>; each variant carries
///   <c>kind: "circle"</c> (literal) for free narrowing.</item>
///   <item>OpenAPI: <c>oneOf</c> + <c>discriminator.mapping</c> — standard
///   3.0 polymorphism shape.</item>
/// </list>
/// </summary>
public class PolymorphicUnionTests
{
    private const string Src = """
        using ZibStack.NET.TypeGen;
        using System.Text.Json.Serialization;
        namespace Poly;

        [GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi, OutputDir = ".")]
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(Circle), "circle")]
        [JsonDerivedType(typeof(Square), "square")]
        public abstract record Shape;

        public record Circle(double Radius) : Shape;
        public record Square(double Side) : Shape;
        """;

    [Fact]
    public void PolymorphicBase_SeedsVariantsIntoModel()
    {
        var model = BuildModel();
        Assert.Contains(model.Classes, c => c.SourceName == "Shape");
        Assert.Contains(model.Classes, c => c.SourceName == "Circle");
        Assert.Contains(model.Classes, c => c.SourceName == "Square");

        var shape = model.Classes.Single(c => c.SourceName == "Shape");
        Assert.Equal("kind", shape.PolymorphicDiscriminator);
        Assert.Equal(2, shape.PolymorphicVariants.Count);

        var circle = model.Classes.Single(c => c.SourceName == "Circle");
        Assert.Equal("circle", circle.PolymorphicDiscriminatorValue);
        Assert.Equal("kind", circle.PolymorphicDiscriminatorPropertyOnVariant);
    }

    [Fact]
    public void TypeScript_Base_EmitsDiscriminatedUnion()
    {
        var model = BuildModel();
        var files = TypeScriptEmitter.Emit(model, new GlobalSettings()).ToList();

        var shapeTs = files.First(f => f.FileName == "Shape.ts").Content;
        Assert.Contains("export type Shape = Circle | Square;", shapeTs);

        var circleTs = files.First(f => f.FileName == "Circle.ts").Content;
        // Variant does NOT extend Shape (Shape is a union, not a struct).
        Assert.DoesNotContain("extends Shape", circleTs);
        // Discriminator pinned to literal + own prop.
        Assert.Contains("kind: \"circle\";", circleTs);
        Assert.Contains("radius: number;", circleTs);
    }

    [Fact]
    public void OpenApi_Base_EmitsOneOfWithDiscriminator()
    {
        var model = BuildModel();
        var yaml = OpenApiEmitter.Emit(model, new GlobalSettings()).Single().Content;

        Assert.Contains("Shape:", yaml);
        Assert.Contains("oneOf:", yaml);
        Assert.Contains("- $ref: '#/components/schemas/Circle'", yaml);
        Assert.Contains("- $ref: '#/components/schemas/Square'", yaml);
        Assert.Contains("discriminator:", yaml);
        Assert.Contains("propertyName: kind", yaml);
        Assert.Contains("circle: '#/components/schemas/Circle'", yaml);
        Assert.Contains("square: '#/components/schemas/Square'", yaml);

        // Variant carries discriminator as a fixed-enum string.
        var circleBlock = yaml.Substring(yaml.IndexOf("Circle:", System.StringComparison.Ordinal));
        Assert.Contains("kind:", circleBlock);
        Assert.Contains("- circle", circleBlock);
    }

    // ── Roslyn harness ──────────────────────────────────────────────────────

    private static SchemaModel BuildModel()
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
            namespace System.Text.Json.Serialization
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
                public sealed class JsonPolymorphicAttribute : System.Attribute {
                    public string? TypeDiscriminatorPropertyName { get; set; }
                }
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                public sealed class JsonDerivedTypeAttribute : System.Attribute {
                    public JsonDerivedTypeAttribute(System.Type derivedType, string typeDiscriminator) {}
                }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "PolyTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(Src) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var model = new SchemaModel();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var sm = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax rd
                    && sm.GetDeclaredSymbol(rd) is INamedTypeSymbol s
                    && SchemaParser.HasGenerateTypes(s))
                {
                    var cls = SchemaParser.ParseClass(s);
                    if (cls is not null) model.Classes.Add(cls);
                }
            }
        }
        int lastCount, guard = 0;
        do
        {
            lastCount = model.Classes.Count + model.Enums.Count;
            SchemaParser.SeedGenericTypeTargets(model, compilation);
            SchemaParser.SeedPolymorphicVariants(model, compilation);
            SchemaParser.DiscoverBaseClasses(model, compilation);
            SchemaParser.DiscoverTransitive(model, compilation);
        } while (model.Classes.Count + model.Enums.Count > lastCount && ++guard < 16);
        SchemaParser.ResolveGenericTypeReferences(model);
        return model;
    }
}
