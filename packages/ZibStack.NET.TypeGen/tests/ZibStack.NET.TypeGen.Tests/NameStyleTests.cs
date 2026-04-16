using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// <c>TypeScriptSettings.PropertyNameStyle</c> + <c>TypeNameStyle</c> reshape
/// identifiers on emission. File names follow the emitted type name, so
/// <c>TypeNameStyle = CamelCase</c> must actually surface as lowercase-initial
/// <c>.ts</c> files on disk — not just inside the interface body.
/// </summary>
public class NameStyleTests
{
    private static SchemaClass Cls(string name) => new()
    {
        CSharpFullName = "Ns." + name,
        SourceName = name,
        EmittedName = name,
        OutputDir = ".",
        Targets = TypeTarget.TypeScript,
    };

    [Fact]
    public void TypeNameStyle_CamelCase_AppliesToFileName_AndTypeDeclaration()
    {
        var cls = Cls("OrderItem");
        cls.Properties.Add(new SchemaProperty { SourceName = "Qty", CSharpTypeFullName = "int" });
        var model = new SchemaModel();
        model.Classes.Add(cls);
        var settings = new GlobalSettings();
        settings.TypeScript.TypeNameStyle = NameStyle.CamelCase;

        var files = TypeScriptEmitter.Emit(model, settings).ToList();
        Assert.Contains(files, f => f.FileName == "orderItem.ts");
        Assert.DoesNotContain(files, f => f.FileName == "OrderItem.ts");
        var content = files.Single(f => f.FileName == "orderItem.ts").Content;
        Assert.Contains("export interface orderItem {", content);
    }

    [Fact]
    public void TypeNameStyle_SnakeCase_AppliesToFileName_AndTypeDeclaration()
    {
        var cls = Cls("OrderItem");
        var model = new SchemaModel();
        model.Classes.Add(cls);
        var settings = new GlobalSettings();
        settings.TypeScript.TypeNameStyle = NameStyle.SnakeCase;

        var files = TypeScriptEmitter.Emit(model, settings).ToList();
        Assert.Contains(files, f => f.FileName == "order_item.ts");
        Assert.Contains("export interface order_item {", files.Single().Content);
    }

    [Fact]
    public void PropertyNameStyle_SnakeCase_AppliesToPropertyLines()
    {
        var cls = Cls("Order");
        cls.Properties.Add(new SchemaProperty { SourceName = "CustomerName", CSharpTypeFullName = "string" });
        cls.Properties.Add(new SchemaProperty { SourceName = "OrderDate", CSharpTypeFullName = "System.DateTime" });
        var model = new SchemaModel();
        model.Classes.Add(cls);
        var settings = new GlobalSettings();
        settings.TypeScript.PropertyNameStyle = NameStyle.SnakeCase;

        var content = TypeScriptEmitter.Emit(model, settings).Single().Content;
        Assert.Contains("customer_name: string;", content);
        Assert.Contains("order_date: string;", content);
    }

    [Fact]
    public void TsName_Override_BypassesTypeNameStyle()
    {
        // Explicit [TsName("...")] is user intent — don't restyle it.
        var cls = Cls("Order");
        cls.TsNameOverride = "OrderDto";
        var model = new SchemaModel();
        model.Classes.Add(cls);
        var settings = new GlobalSettings();
        settings.TypeScript.TypeNameStyle = NameStyle.CamelCase;

        var files = TypeScriptEmitter.Emit(model, settings).ToList();
        Assert.Contains(files, f => f.FileName == "OrderDto.ts");
    }

    [Fact]
    public void SingleFile_TypeNameStyle_CamelCase_AppliesToBody()
    {
        var cls = Cls("Order");
        cls.Properties.Add(new SchemaProperty { SourceName = "Id", CSharpTypeFullName = "int" });
        var model = new SchemaModel();
        model.Classes.Add(cls);
        var settings = new GlobalSettings();
        settings.TypeScript.TypeNameStyle = NameStyle.CamelCase;
        settings.TypeScript.FileLayout = TypeScriptFileLayout.SingleFile;

        var file = TypeScriptEmitter.Emit(model, settings).Single();
        // Single-file naming still respects SingleFileName, but the body uses styled names.
        Assert.Contains("export interface order {", file.Content);
    }
}
