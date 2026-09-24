using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

public sealed class ZodSchemaOverrideTests
{
    [Fact]
    public void CompoundImportWithoutName_ReportsActionableDiagnostic()
    {
        var model = new SchemaModel();
        var cls = new SchemaClass
        {
            SourceName = "Order", CSharpFullName = "Order", EmittedName = "Order",
            Targets = ZibStack.NET.TypeGen.Generator.TypeTarget.Zod,
        };
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Payload", CSharpTypeFullName = "object",
            ZodSchemaOverride = "payloadSchema.nullable()",
            ZodSchemaImportFrom = "@acme/schemas",
        });
        model.Classes.Add(cls);
        var diagnostics = new System.Collections.Generic.List<Diagnostic>();

        Assert.False(TypeGenGenerator.ValidateZodSchemaImports(model, diagnostics.Add));
        Assert.Equal(TypeGenDiagnostics.ZodSchemaImportRequiredId, Assert.Single(diagnostics).Id);

        cls.Properties[0].ZodSchemaImport = "payloadSchema";
        diagnostics.Clear();
        Assert.True(TypeGenGenerator.ValidateZodSchemaImports(model, diagnostics.Add));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Attribute_IsParsedAndEmittedWithItsImport()
    {
        const string source = """
            #nullable enable
            using ZibStack.NET.TypeGen;

            [GenerateTypes(Targets = TypeTarget.Zod)]
            public sealed class Order
            {
                [ZodSchema("GeoJSONPointSchema.nullable()", ImportFrom = "zod-geojson", Import = "GeoJSONPointSchema")]
                public object? DeliveryPoint { get; init; }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var refs = ((string)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ZodSchemaAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "ZodSchemaAttributeTest",
            new[] { syntaxTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var cls = SchemaParser.ParseClass(compilation.GetTypeByMetadataName("Order")!)!;
        var property = Assert.Single(cls.Properties);
        Assert.Equal("GeoJSONPointSchema.nullable()", property.ZodSchemaOverride);
        Assert.Equal("zod-geojson", property.ZodSchemaImportFrom);
        Assert.Equal("GeoJSONPointSchema", property.ZodSchemaImport);

        var model = new SchemaModel();
        model.Classes.Add(cls);
        var output = ZodEmitter.Emit(model, new GlobalSettings()).Single().Content;
        Assert.Contains("import { GeoJSONPointSchema } from 'zod-geojson';", output);
        Assert.Contains("deliveryPoint: (GeoJSONPointSchema.nullable()).nullish()", output);
    }
}
