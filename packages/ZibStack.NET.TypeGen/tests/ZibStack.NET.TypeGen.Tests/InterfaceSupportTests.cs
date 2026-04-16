using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Interfaces were historically ignored by TypeGen — neither emitted as their
/// own schemas nor wired into implementing classes. These tests pin down the
/// behaviour we want instead:
/// <list type="bullet">
///   <item>Interfaces with members get emitted (TS <c>interface</c> / OpenAPI
///         schema) when annotated, or auto-seeded when reached transitively
///         from a <c>[GenerateTypes]</c> class.</item>
///   <item>Implementing classes mirror the C# structure — TS <c>extends I</c> /
///         OpenAPI <c>allOf: [$ref I, …]</c> — instead of re-declaring
///         inherited members.</item>
///   <item><c>[TsIgnore]</c> / <c>[OpenApiIgnore]</c> on an interface member
///         propagates naturally: the member doesn't land in the interface
///         schema, so nothing generated pulls it in.</item>
///   <item>Marker interfaces (no members) stay silently skipped — emitting an
///         empty schema serves nobody.</item>
/// </list>
/// </summary>
public class InterfaceSupportTests
{
    [Fact]
    public void AnnotatedInterface_EmittedAsStandalone()
    {
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            [GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi, OutputDir = ".")]
            public interface IAuditable
            {
                System.DateTime CreatedAt { get; set; }
                string ModifiedBy { get; set; }
            }
            """;
        var model = BuildModel(src, "Iface.IAuditable");
        var iface = model.Classes.Single(c => c.SourceName == "IAuditable");
        Assert.Contains(iface.Properties, p => p.SourceName == "CreatedAt");
        Assert.Contains(iface.Properties, p => p.SourceName == "ModifiedBy");

        var ts = TypeScriptEmitter.Emit(model, EmitSettings())
            .Single(f => f.FileName == "IAuditable.ts").Content;
        Assert.Contains("export interface IAuditable", ts);
        Assert.Contains("createdAt: string;", ts);
        Assert.Contains("modifiedBy: string;", ts);

        var yaml = OpenApiEmitter.Emit(model, EmitSettings()).Single().Content;
        Assert.Contains("IAuditable:", yaml);
    }

    [Fact]
    public void ClassImplementingAnnotatedInterface_ExtendsIt_DoesNotRedeclareMembers()
    {
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            [GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi, OutputDir = ".")]
            public interface IAuditable
            {
                System.DateTime CreatedAt { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi, OutputDir = ".")]
            public class Order : IAuditable
            {
                public int Id { get; set; }
                public System.DateTime CreatedAt { get; set; }
            }
            """;
        var model = BuildModel(src, "Iface.Order", "Iface.IAuditable");
        var order = model.Classes.Single(c => c.SourceName == "Order");

        // Model still carries the C# declaration faithfully; dedupe is the
        // emitter's job (per-target, respecting ignore flags).
        Assert.Contains(order.ImplementedInterfaces, i => i.EndsWith("IAuditable"));

        var ts = TypeScriptEmitter.Emit(model, EmitSettings())
            .Single(f => f.FileName == "Order.ts").Content;
        Assert.Contains("export interface Order extends IAuditable", ts);
        Assert.Contains("id: number;", ts);
        // Inherited member not redeclared in the class output for TS.
        Assert.DoesNotContain("createdAt:", ts);

        var yaml = OpenApiEmitter.Emit(model, EmitSettings()).Single().Content;
        // OpenAPI: allOf [$ref IAuditable, type: object with Id only].
        Assert.Matches(@"Order:\r?\n\s+allOf:\r?\n\s+- \$ref: '#/components/schemas/IAuditable'", yaml);
    }

    [Fact]
    public void UnannotatedInterface_AutoSeededFromImplementingClass()
    {
        // Mirror of DiscoverBaseClasses for interfaces — the user shouldn't have
        // to annotate every interface they touch; putting [GenerateTypes] on the
        // DTO pulls its implemented interfaces into the same emit set.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            public interface IAuditable { System.DateTime CreatedAt { get; set; } }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Order : IAuditable
            {
                public int Id { get; set; }
                public System.DateTime CreatedAt { get; set; }
            }
            """;
        var model = BuildModel(src, "Iface.Order");
        Assert.Contains(model.Classes, c => c.SourceName == "IAuditable");
        var iface = model.Classes.Single(c => c.SourceName == "IAuditable");
        Assert.Contains(iface.Properties, p => p.SourceName == "CreatedAt");
        // Targets / OutputDir inherited from the root that reached it.
        Assert.True((iface.Targets & TypeTarget.TypeScript) != 0);
    }

    [Fact]
    public void IgnoreOnInterfaceMember_Propagates_ViaInterfaceSchemaNotCarryingMember()
    {
        // The user's pain point: [OpenApiIgnore] on an interface property didn't
        // take effect because the interface wasn't emitted as a schema at all,
        // and implementing classes re-declared the property from their own
        // syntax (attribute on the interface never surfaced). Now the interface
        // IS a schema, so the ignore drops the property from the interface
        // schema — and because the class no longer re-declares inherited
        // members (covered by `extends`/`allOf`), the property never appears
        // in the OpenAPI output at all.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            public interface IAuditable
            {
                System.DateTime CreatedAt { get; set; }
                [OpenApiIgnore]
                string InternalTrace { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.OpenApi, OutputDir = ".")]
            public class Order : IAuditable
            {
                public int Id { get; set; }
                public System.DateTime CreatedAt { get; set; }
                public string InternalTrace { get; set; } = "";
            }
            """;
        var model = BuildModel(src, "Iface.Order");
        var yaml = OpenApiEmitter.Emit(model, EmitSettings()).Single().Content;

        // IAuditable schema has CreatedAt, not InternalTrace.
        var iauditBlock = ExtractSchemaBlock(yaml, "IAuditable");
        Assert.Contains("CreatedAt", iauditBlock);
        Assert.DoesNotContain("InternalTrace", iauditBlock);

        // Order schema doesn't redeclare CreatedAt (extends covers it) AND
        // doesn't include InternalTrace at all — because the inherited member
        // is already known to the extends chain and the interface dropped it.
        var orderBlock = ExtractSchemaBlock(yaml, "Order");
        Assert.DoesNotContain("CreatedAt", orderBlock);
        Assert.DoesNotContain("InternalTrace", orderBlock);
    }

    [Fact]
    public void MarkerInterface_SilentlySkipped()
    {
        // An empty interface contributes nothing to the schema graph. Emitting
        // an empty `type: object` wastes a slot in components/schemas and just
        // pollutes auto-generated consumer code.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            public interface IMarker {}

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Thing : IMarker
            {
                public int Id { get; set; }
            }
            """;
        var model = BuildModel(src, "Iface.Thing");
        Assert.DoesNotContain(model.Classes, c => c.SourceName == "IMarker");

        var ts = TypeScriptEmitter.Emit(model, EmitSettings())
            .Single(f => f.FileName == "Thing.ts").Content;
        // No extends for a marker.
        Assert.DoesNotContain("extends IMarker", ts);
    }

    [Fact]
    public void GenericBase_WithInterface_InterfaceEmitted_BaseExtendsIt()
    {
        // User's reported scenario: `Base<T> : ISomeContract`. Both Base<T> and
        // ISomeContract should end up as schemas, with Base<T> extending the
        // interface.
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            public interface ISomeContract { string Name { get; set; } }
            public class Base<T> : ISomeContract
            {
                public T Payload { get; set; } = default!;
                public string Name { get; set; } = "";
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Derived : Base<int>
            {
                public int Extra { get; set; }
            }
            """;
        var model = BuildModel(src, "Iface.Derived");

        Assert.Contains(model.Classes, c => c.SourceName == "ISomeContract");
        Assert.Contains(model.Classes, c => c.SourceName == "Base");
        Assert.Contains(model.Classes, c => c.SourceName == "Derived");

        var baseSchema = model.Classes.Single(c => c.SourceName == "Base");
        Assert.Contains(baseSchema.ImplementedInterfaces, i => i.EndsWith("ISomeContract"));

        var ts = TypeScriptEmitter.Emit(model, EmitSettings()).ToList();
        var baseTs = ts.First(f => f.FileName == "Base.ts").Content;
        Assert.Contains("export interface Base<T> extends ISomeContract", baseTs);
        // Name lives on ISomeContract — Base<T> shouldn't redeclare it in TS.
        Assert.DoesNotContain("name: string;", baseTs);
        Assert.Contains("payload: T;", baseTs);
    }

    [Fact]
    public void MultipleInterfaces_AllListedInExtends()
    {
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            public interface IHasId { int Id { get; set; } }
            public interface IHasName { string Name { get; set; } }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Widget : IHasId, IHasName
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public int Extra { get; set; }
            }
            """;
        var model = BuildModel(src, "Iface.Widget");
        var widget = model.Classes.Single(c => c.SourceName == "Widget");
        Assert.Equal(2, widget.ImplementedInterfaces.Count);

        var ts = TypeScriptEmitter.Emit(model, EmitSettings())
            .Single(f => f.FileName == "Widget.ts").Content;
        // Both interfaces listed after extends; order not asserted — pipeline may sort.
        Assert.Matches(@"export interface Widget extends (IHasId, IHasName|IHasName, IHasId)", ts);
        // Inherited members not re-declared in TS — only Extra remains.
        Assert.Contains("extra: number;", ts);
        Assert.DoesNotContain("id: number;", ts);
        Assert.DoesNotContain("name: string;", ts);
    }

    [Fact]
    public void TsIgnoreOnInterface_DropsItFromExtendsList()
    {
        const string src = """
            using ZibStack.NET.TypeGen;
            namespace Iface;

            [TsIgnore]
            public interface IHiddenInTs { string Secret { get; set; } }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Thing : IHiddenInTs
            {
                public int Id { get; set; }
                public string Secret { get; set; } = "";
            }
            """;
        var model = BuildModel(src, "Iface.Thing");
        var ts = TypeScriptEmitter.Emit(model, EmitSettings())
            .Single(f => f.FileName == "Thing.ts").Content;

        // TsIgnore on the interface removes it from the TS extends list.
        // Without an emittable ancestor carrying Secret, the class takes it
        // back and inlines it — otherwise it'd be silently lost.
        Assert.DoesNotContain("extends IHiddenInTs", ts);
        Assert.Contains("secret: string;", ts);
    }

    // ── harness ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Opts into interface emission via the TypeScriptSettings flag — single
    /// knob for all three targets (TS/OpenAPI/Python pick up the same bit).
    /// </summary>
    private static GlobalSettings EmitSettings()
        => new() { TypeScript = new TypeScriptSettings { EmitInterfaces = true } };

    private static string ExtractSchemaBlock(string yaml, string schemaName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            yaml,
            @"\n    " + System.Text.RegularExpressions.Regex.Escape(schemaName) + @":\r?\n([\s\S]*?)(?=\n    [A-Z]|\z)");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static SchemaModel BuildModel(string userSource, params string[] rootMetadataNames)
    {
        const string stubs = """
            namespace ZibStack.NET.TypeGen
            {
                [System.Flags] public enum TypeTarget { None = 0, TypeScript = 1, OpenApi = 2, Python = 4 }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Enum | System.AttributeTargets.Interface)]
                public sealed class GenerateTypesAttribute : System.Attribute {
                    public TypeTarget Targets { get; set; }
                    public string? OutputDir { get; set; }
                }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface | System.AttributeTargets.Property)]
                public sealed class TsIgnoreAttribute : System.Attribute {}
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface | System.AttributeTargets.Property)]
                public sealed class OpenApiIgnoreAttribute : System.Attribute {}
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.DateTime).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "IfaceTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(userSource) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var model = new SchemaModel();
        foreach (var name in rootMetadataNames)
        {
            var sym = compilation.GetTypeByMetadataName(name);
            Assert.NotNull(sym);
            var cls = SchemaParser.ParseClass(sym!);
            if (cls is not null) model.Classes.Add(cls);
        }

        // Full pipeline — interface discovery runs in the same fixpoint loop as
        // base-class / polymorphic / generic seeding. Iterate until stable.
        int lastCount, guard = 0;
        do
        {
            lastCount = model.Classes.Count + model.Enums.Count;
            SchemaParser.SeedGenericTypeTargets(model, compilation);
            SchemaParser.SeedPolymorphicVariants(model, compilation);
            SchemaParser.DiscoverBaseClasses(model, compilation);
            SchemaParser.DiscoverInterfaces(model, compilation);
            SchemaParser.DiscoverTransitive(model, compilation);
        } while (model.Classes.Count + model.Enums.Count > lastCount && ++guard < 16);
        SchemaParser.ResolveGenericTypeReferences(model);
        return model;
    }
}
