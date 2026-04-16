using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// When an enum carries <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c>
/// (or the generic <c>JsonStringEnumConverter&lt;T&gt;</c>, or Newtonsoft's
/// <c>StringEnumConverter</c>), the runtime JSON is strings, not numbers. The
/// generated TS / Python / OpenAPI output has to match that, otherwise client
/// code deserialises into the wrong shape. Without the converter the defaults
/// stay (numeric TS enum, <c>IntEnum</c> in Python).
/// </summary>
public class JsonStringEnumConverterTests
{
    [Fact]
    public void StjConverter_OnEnum_SetsStringSerializedFlag()
    {
        var en = ParseEnum("""
            using System.Text.Json.Serialization;
            using ZibStack.NET.TypeGen;
            namespace N;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public enum Status { Pending, Shipped }
            """, "N.Status");

        Assert.True(en.IsStringSerialized);
    }

    [Fact]
    public void StjGenericConverter_OnEnum_SetsStringSerializedFlag()
    {
        // .NET 8+: the generic form `JsonStringEnumConverter<Status>`.
        var en = ParseEnum("""
            using System.Text.Json.Serialization;
            using ZibStack.NET.TypeGen;
            namespace N;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            [JsonConverter(typeof(JsonStringEnumConverter<Status>))]
            public enum Status { Pending, Shipped }
            """, "N.Status");

        Assert.True(en.IsStringSerialized);
    }

    [Fact]
    public void NewtonsoftStringEnumConverter_SetsStringSerializedFlag()
    {
        var en = ParseEnum("""
            using Newtonsoft.Json;
            using Newtonsoft.Json.Converters;
            using ZibStack.NET.TypeGen;
            namespace N;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            [JsonConverter(typeof(StringEnumConverter))]
            public enum Status { Pending, Shipped }
            """, "N.Status");

        Assert.True(en.IsStringSerialized);
    }

    [Fact]
    public void NoConverter_FlagStaysFalse()
    {
        var en = ParseEnum("""
            using ZibStack.NET.TypeGen;
            namespace N;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public enum Status { Pending, Shipped }
            """, "N.Status");

        Assert.False(en.IsStringSerialized);
    }

    [Fact]
    public void OtherJsonConverter_FlagStaysFalse()
    {
        // A random custom converter that isn't one of the known stringifying ones —
        // we don't assume anything about its serialized form.
        var en = ParseEnum("""
            using System.Text.Json.Serialization;
            using ZibStack.NET.TypeGen;
            namespace N;

            public sealed class SomeOtherConverter : JsonConverter<Status> {
                public override Status Read(ref System.Text.Json.Utf8JsonReader r, System.Type t, JsonSerializerOptions o) => default;
                public override void Write(System.Text.Json.Utf8JsonWriter w, Status v, JsonSerializerOptions o) {}
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            [JsonConverter(typeof(SomeOtherConverter))]
            public enum Status { Pending, Shipped }
            """, "N.Status");

        Assert.False(en.IsStringSerialized);
    }

    // ── Emitter output ──────────────────────────────────────────────────────

    [Fact]
    public void TypeScriptEmitter_StringSerializedEnum_EmitsStringValues()
    {
        var en = new SchemaEnum
        {
            CSharpFullName = "N.Status", SourceName = "Status", EmittedName = "Status",
            OutputDir = ".", Targets = TypeTarget.TypeScript,
            IsStringSerialized = true,
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        var model = new SchemaModel();
        model.Enums.Add(en);

        var ts = TypeScriptEmitter.Emit(model, new GlobalSettings()).First(f => f.FileName == "Status.ts").Content;
        Assert.Contains("export enum Status {", ts);
        Assert.Contains("Pending = \"Pending\",", ts);
        Assert.Contains("Shipped = \"Shipped\",", ts);
        Assert.DoesNotContain("= 0,", ts);
        Assert.DoesNotContain("= 1,", ts);
    }

    [Fact]
    public void TypeScriptEmitter_NumericEnum_UnchangedBehavior()
    {
        // Baseline — IsStringSerialized=false still produces numeric values.
        var en = new SchemaEnum
        {
            CSharpFullName = "N.Status", SourceName = "Status", EmittedName = "Status",
            OutputDir = ".", Targets = TypeTarget.TypeScript,
            IsStringSerialized = false,
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        var model = new SchemaModel();
        model.Enums.Add(en);

        var ts = TypeScriptEmitter.Emit(model, new GlobalSettings()).First(f => f.FileName == "Status.ts").Content;
        Assert.Contains("Pending = 0,", ts);
        Assert.Contains("Shipped = 1,", ts);
    }

    [Fact]
    public void PythonEmitter_StringSerializedEnum_EmitsStrEnumPattern()
    {
        var en = new SchemaEnum
        {
            CSharpFullName = "N.Status", SourceName = "Status", EmittedName = "Status",
            OutputDir = ".", Targets = TypeTarget.Python,
            IsStringSerialized = true,
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        var model = new SchemaModel();
        model.Enums.Add(en);

        var py = TypeScriptEmitter_PythonShim(model);

        // `class Status(str, Enum):` works across Python 3.8+ (StrEnum arrived in 3.11).
        Assert.Contains("class Status(str, Enum):", py);
        Assert.Contains("PENDING = \"Pending\"", py);
        Assert.Contains("SHIPPED = \"Shipped\"", py);
    }

    [Fact]
    public void PythonEmitter_NumericEnum_UnchangedBehavior()
    {
        var en = new SchemaEnum
        {
            CSharpFullName = "N.Status", SourceName = "Status", EmittedName = "Status",
            OutputDir = ".", Targets = TypeTarget.Python,
            IsStringSerialized = false,
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        var model = new SchemaModel();
        model.Enums.Add(en);

        var py = TypeScriptEmitter_PythonShim(model);
        Assert.Contains("class Status(IntEnum):", py);
        Assert.Contains("PENDING = 0", py);
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static string TypeScriptEmitter_PythonShim(SchemaModel model)
    {
        var files = PythonEmitter.Emit(model, new GlobalSettings());
        return string.Join("\n\n", files.Select(f => f.Content));
    }

    private static SchemaEnum ParseEnum(string src, string fqn)
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
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Enum | System.AttributeTargets.Property | System.AttributeTargets.Struct)]
                public sealed class JsonConverterAttribute : System.Attribute {
                    public System.Type? ConverterType { get; }
                    public JsonConverterAttribute(System.Type converterType) => ConverterType = converterType;
                }
                public abstract class JsonConverter<T> {
                    public abstract T Read(ref System.Text.Json.Utf8JsonReader r, System.Type t, JsonSerializerOptions o);
                    public abstract void Write(System.Text.Json.Utf8JsonWriter w, T v, JsonSerializerOptions o);
                }
                public sealed class JsonStringEnumConverter { }
                public sealed class JsonStringEnumConverter<TEnum> where TEnum : struct, System.Enum { }
                public class JsonSerializerOptions { }
            }
            namespace System.Text.Json { public ref struct Utf8JsonReader {} public ref struct Utf8JsonWriter {} }
            namespace Newtonsoft.Json
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Enum | System.AttributeTargets.Property)]
                public sealed class JsonConverterAttribute : System.Attribute {
                    public System.Type? ConverterType { get; }
                    public JsonConverterAttribute(System.Type converterType) => ConverterType = converterType;
                }
            }
            namespace Newtonsoft.Json.Converters
            {
                public sealed class StringEnumConverter { }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "JsonStringEnumConverterTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(src) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var sym = compilation.GetTypeByMetadataName(fqn);
        Assert.NotNull(sym);
        var parsed = SchemaParser.ParseEnum(sym!);
        Assert.NotNull(parsed);
        return parsed!;
    }
}
