using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

// Namespace intentionally OUTSIDE ZibStack.NET.TypeGen.* — otherwise the sibling
// ZibStack.NET.TypeGen (Abstractions) gets implicitly imported and the public
// TypeTarget enum collides with the internal mirror we want to test against.
namespace TypeGenTests;

/// <summary>
/// Unit tests for the TypeScript emitter — builds a <see cref="SchemaModel"/> by hand,
/// runs the emitter, asserts on the produced text. No Roslyn involvement, no file I/O —
/// just verifies the text-generation half of the pipeline.
/// </summary>
public class TypeScriptEmitterTests
{
    private static SchemaClass Cls(string name, string outputDir = "out",
        TypeTarget targets = TypeTarget.TypeScript,
        params (string Name, string CSharpType, bool Nullable)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name,
            SourceName = name,
            EmittedName = name,
            OutputDir = outputDir,
            Targets = targets,
        };
        foreach (var (n, t, nu) in props)
            c.Properties.Add(new SchemaProperty { SourceName = n, CSharpTypeFullName = t, IsNullable = nu });
        return c;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var m = new SchemaModel();
        m.Classes.AddRange(classes);
        return m;
    }

    // ── primitive mapping ──

    [Fact]
    public void IntStringBoolDecimal_MapToExpectedTsTypes()
    {
        var cls = Cls("Order",
            props: new[]
            {
                ("Id", "int", false),
                ("Name", "string", false),
                ("Active", "bool", false),
                ("Price", "decimal", false),
            });
        var files = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings());
        var ts = files.Single().Content;

        Assert.Contains("id: number;", ts);
        Assert.Contains("name: string;", ts);
        Assert.Contains("active: boolean;", ts);
        Assert.Contains("price: string;", ts);   // decimal → string on purpose
    }

    [Fact]
    public void Nullable_EmitsOptionalMarker_AndUnionWithUndefined()
    {
        var cls = Cls("Order", props: new[] { ("Note", "string", true) });
        var files = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings());
        var ts = files.Single().Content;

        Assert.Contains("note?: string;", ts);
    }

    [Fact]
    public void GuidDateTime_MapToString()
    {
        var cls = Cls("Entity",
            props: new[]
            {
                ("Id", "System.Guid", false),
                ("Created", "System.DateTime", false),
                ("Offset", "System.DateTimeOffset", true),
            });
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("id: string;", ts);
        Assert.Contains("created: string;", ts);
        Assert.Contains("offset?: string;", ts);
    }

    // ── collections ──

    [Fact]
    public void ListOfT_MapsToArrayOfTs()
    {
        var cls = Cls("Order", props: new[] { ("Tags", "System.Collections.Generic.List<string>", false) });
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("tags: string[];", ts);
    }

    [Fact]
    public void Dictionary_MapsToRecord()
    {
        var cls = Cls("Order", props: new[] { ("Meta", "System.Collections.Generic.Dictionary<string, int>", false) });
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("meta: Record<string, number>;", ts);
    }

    // ── cross-reference resolution ──

    [Fact]
    public void UserDtoReference_ResolvesToEmittedTsName()
    {
        var item = Cls("OrderItem");
        var order = Cls("Order", props: new[]
        {
            ("Item", "OrderItem", false),
            ("Items", "System.Collections.Generic.List<OrderItem>", false),
        });
        var files = TypeScriptEmitter.Emit(ModelWith(order, item), new GlobalSettings());
        // file-per-class layout — find the Order file
        var orderTs = files.First(f => f.FileName == "Order.ts").Content;

        Assert.Contains("item: OrderItem;", orderTs);
        Assert.Contains("items: OrderItem[];", orderTs);
    }

    // ── overrides ──

    [Fact]
    public void TsNameOverride_OnProperty_WinsOverCamelCaseRule()
    {
        var cls = Cls("Order", props: new[] { ("Id", "int", false) });
        cls.Properties[0].TsNameOverride = "orderId";
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("orderId: number;", ts);
    }

    [Fact]
    public void TsTypeOverride_SubstitutesTypeExpression()
    {
        var cls = Cls("Order", props: new[] { ("Status", "int", false) });
        cls.Properties[0].TsTypeOverride = "'pending' | 'shipped'";
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("status: 'pending' | 'shipped';", ts);
    }

    [Fact]
    public void TsIgnore_OnProperty_SkipsIt()
    {
        var cls = Cls("Order", props: new[] { ("Id", "int", false), ("Secret", "string", false) });
        cls.Properties[1].TsIgnore = true;
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("id: number;", ts);
        Assert.DoesNotContain("secret", ts);
    }

    [Fact]
    public void TsIgnore_OnClass_ProducesNoFile()
    {
        var cls = Cls("Order");
        cls.TsIgnore = true;
        var files = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings());
        Assert.Empty(files);
    }

    // ── file layout ──

    [Fact]
    public void SingleFileLayout_MergesEverythingIntoOneFile()
    {
        var settings = new GlobalSettings
        {
            TypeScript = { FileLayout = TypeScriptFileLayout.SingleFile, SingleFileName = "models.ts", OutputDir = "out" },
        };
        var files = TypeScriptEmitter.Emit(ModelWith(Cls("Order"), Cls("User")), settings);
        var f = Assert.Single(files);
        Assert.Equal("models.ts", f.FileName);
        Assert.Contains("Order", f.Content);
        Assert.Contains("User", f.Content);
    }

    [Fact]
    public void FilePerClassLayout_OneFilePerType()
    {
        var files = TypeScriptEmitter.Emit(ModelWith(Cls("Order"), Cls("User")), new GlobalSettings());
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.FileName == "Order.ts");
        Assert.Contains(files, f => f.FileName == "User.ts");
    }

    // ── strip suffixes ──

    [Fact]
    public void StripSuffixes_RemovesDtoSuffixFromTypeName()
    {
        var settings = new GlobalSettings();
        settings.TypeScript.StripSuffixes.Add("Dto");
        var cls = Cls("OrderDto", props: new[] { ("Id", "int", false) });
        var files = TypeScriptEmitter.Emit(ModelWith(cls), settings);
        var f = Assert.Single(files);
        Assert.Equal("Order.ts", f.FileName);
        Assert.Contains("export interface Order {", f.Content);
        Assert.DoesNotContain("OrderDto", f.Content);
    }

    // ── interface vs type alias ──

    [Fact]
    public void UseInterfaces_False_EmitsTypeAlias()
    {
        var settings = new GlobalSettings();
        settings.TypeScript.UseInterfaces = false;
        var cls = Cls("Order", props: new[] { ("Id", "int", false) });
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), settings).Single().Content;
        Assert.Contains("export type Order = {", ts);
        Assert.Contains("};", ts);
    }

    // ── enums ──

    // ── cross-file imports (regression: missing in initial MVP) ──

    [Fact]
    public void FilePerClass_ReferencedUserDto_GetsImport()
    {
        // Order references OrderItem in a property type. With file-per-class layout
        // the TS compiler needs `import { OrderItem } from './OrderItem';` at the top
        // of Order.ts or the TSC call in the consumer project fails.
        var item = Cls("OrderItem");
        var order = Cls("Order", props: new[] { ("Item", "OrderItem", false) });
        var files = TypeScriptEmitter.Emit(ModelWith(order, item), new GlobalSettings());
        var orderTs = files.First(f => f.FileName == "Order.ts").Content;

        Assert.Contains("import { OrderItem } from './OrderItem';", orderTs);
    }

    [Fact]
    public void FilePerClass_ReferencedEnum_GetsImport()
    {
        var model = new SchemaModel();
        model.Classes.Add(Cls("Order", props: new[] { ("Status", "OrderStatus", false) }));
        var en = new SchemaEnum
        {
            CSharpFullName = "OrderStatus", SourceName = "OrderStatus",
            EmittedName = "OrderStatus", Targets = TypeTarget.TypeScript, OutputDir = ".",
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        model.Enums.Add(en);

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings());
        var orderTs = files.First(f => f.FileName == "Order.ts").Content;

        Assert.Contains("import { OrderStatus } from './OrderStatus';", orderTs);
    }

    [Fact]
    public void FilePerClass_ArrayOfReferencedDto_GetsImport()
    {
        var item = Cls("OrderItem");
        var order = Cls("Order", props: new[]
            { ("Items", "System.Collections.Generic.List<OrderItem>", false) });
        var files = TypeScriptEmitter.Emit(ModelWith(order, item), new GlobalSettings());
        var orderTs = files.First(f => f.FileName == "Order.ts").Content;

        Assert.Contains("import { OrderItem } from './OrderItem';", orderTs);
    }

    [Fact]
    public void FilePerClass_NoCrossReferences_NoImports()
    {
        var cls = Cls("Simple", props: new[] { ("Id", "int", false), ("Name", "string", false) });
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.DoesNotContain("import ", ts);
    }

    [Fact]
    public void FilePerClass_SelfReference_DoesNotImportItself()
    {
        var node = Cls("TreeNode", props: new[] { ("Parent", "TreeNode", true) });
        var ts = TypeScriptEmitter.Emit(ModelWith(node), new GlobalSettings()).Single().Content;

        Assert.DoesNotContain("import { TreeNode }", ts);
    }

    [Fact]
    public void SingleFileLayout_NoImports_EvenWithCrossReferences()
    {
        var settings = new GlobalSettings
        {
            TypeScript = { FileLayout = TypeScriptFileLayout.SingleFile, SingleFileName = "models.ts" },
        };
        var item = Cls("OrderItem");
        var order = Cls("Order", props: new[] { ("Item", "OrderItem", false) });
        var ts = TypeScriptEmitter.Emit(ModelWith(order, item), settings).Single().Content;

        Assert.DoesNotContain("import ", ts);
    }

    [Fact]
    public void Enum_EmitsWithNumericValues()
    {
        var model = new SchemaModel();
        var en = new SchemaEnum
        {
            CSharpFullName = "Status",
            SourceName = "Status",
            EmittedName = "Status",
            Targets = TypeTarget.TypeScript,
            OutputDir = "out",
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Done", Value = 1 });
        model.Enums.Add(en);

        var ts = TypeScriptEmitter.Emit(model, new GlobalSettings()).Single().Content;

        Assert.Contains("export enum Status {", ts);
        Assert.Contains("Pending = 0,", ts);
        Assert.Contains("Done = 1,", ts);
    }
}
