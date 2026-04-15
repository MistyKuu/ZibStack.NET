using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Verifies that <see cref="OpenApiEmitter"/> emits the schema constraints
/// (<c>minLength</c>, <c>maxLength</c>, <c>minimum</c>, <c>maximum</c>,
/// <c>pattern</c>) populated from DataAnnotations / ZibStack.Validation
/// attributes. These tests drive the emitter directly with pre-filled
/// SchemaProperty values — the attribute-reading side lives in SchemaParser
/// and is exercised indirectly via the E2E SampleApi build test.
/// </summary>
public class ValidationMappingTests
{
    private static SchemaClass Cls(string name, params SchemaProperty[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.OpenApi,
        };
        c.Properties.AddRange(props);
        return c;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var m = new SchemaModel();
        m.Classes.AddRange(classes);
        return m;
    }

    [Fact]
    public void MinMaxLength_EmittedAsStringConstraints()
    {
        var prop = new SchemaProperty
        {
            SourceName = "Name", CSharpTypeFullName = "string",
            MinLength = 2, MaxLength = 50,
        };
        var yaml = OpenApiEmitter.Emit(ModelWith(Cls("Order", prop)), new GlobalSettings()).Single().Content;
        Assert.Contains("minLength: 2", yaml);
        Assert.Contains("maxLength: 50", yaml);
    }

    [Fact]
    public void MinimumMaximum_EmittedAsNumericBounds()
    {
        var prop = new SchemaProperty
        {
            SourceName = "Level", CSharpTypeFullName = "int",
            Minimum = 1, Maximum = 100,
        };
        var yaml = OpenApiEmitter.Emit(ModelWith(Cls("Player", prop)), new GlobalSettings()).Single().Content;
        Assert.Contains("minimum: 1", yaml);
        Assert.Contains("maximum: 100", yaml);
        // Whole-number formatting: no trailing '.0'.
        Assert.DoesNotContain("minimum: 1.0", yaml);
        Assert.DoesNotContain("maximum: 100.0", yaml);
    }

    [Fact]
    public void Pattern_EmittedAsRegexString()
    {
        var prop = new SchemaProperty
        {
            SourceName = "Slug", CSharpTypeFullName = "string",
            Pattern = "^[a-z0-9-]+$",
        };
        var yaml = OpenApiEmitter.Emit(ModelWith(Cls("Article", prop)), new GlobalSettings()).Single().Content;
        Assert.Contains("pattern: \"^[a-z0-9-]+$\"", yaml);
    }

    [Fact]
    public void FractionalBounds_KeepDecimals()
    {
        // Ratio in [0.0, 1.0] — the emitter must not strip decimals on fractional values.
        var prop = new SchemaProperty
        {
            SourceName = "Ratio", CSharpTypeFullName = "double",
            Minimum = 0.0, Maximum = 0.95,
        };
        var yaml = OpenApiEmitter.Emit(ModelWith(Cls("Metric", prop)), new GlobalSettings()).Single().Content;
        Assert.Contains("minimum: 0", yaml);   // 0.0 → 0 is OK, no decimal needed
        Assert.Contains("maximum: 0.95", yaml);
    }

    [Fact]
    public void JsonOutput_IncludesAllConstraints()
    {
        var settings = new GlobalSettings();
        settings.OpenApi.OutputPath = "openapi.json";
        var prop = new SchemaProperty
        {
            SourceName = "Name", CSharpTypeFullName = "string",
            MinLength = 2, MaxLength = 50, Pattern = "^[A-Z].*$",
        };
        var json = OpenApiEmitter.Emit(ModelWith(Cls("Order", prop)), settings).Single().Content;
        Assert.Contains("\"minLength\": 2", json);
        Assert.Contains("\"maxLength\": 50", json);
        Assert.Contains("\"pattern\": \"^[A-Z].*$\"", json);
    }
}
