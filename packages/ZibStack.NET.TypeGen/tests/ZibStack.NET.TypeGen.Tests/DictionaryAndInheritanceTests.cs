using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Exercises the Dictionary → <c>additionalProperties</c> mapping and the
/// inheritance expression (<c>allOf</c> in OpenAPI, <c>extends</c> in TS).
/// </summary>
public class DictionaryAndInheritanceTests
{
    private static SchemaClass Cls(string name, params (string Name, string Type)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
        };
        foreach (var (n, t) in props)
            c.Properties.Add(new SchemaProperty { SourceName = n, CSharpTypeFullName = t });
        return c;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var m = new SchemaModel();
        m.Classes.AddRange(classes);
        return m;
    }

    [Fact]
    public void Dictionary_EmitsAdditionalProperties()
    {
        var cls = Cls("Settings", ("Values", "System.Collections.Generic.Dictionary<string, int>"));
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("Values:", yaml);
        Assert.Contains("type: object", yaml);
        Assert.Contains("additionalProperties:", yaml);
        Assert.Contains("type: integer", yaml);
    }

    [Fact]
    public void DictionaryOfUserDto_UsesRefForValue()
    {
        var tag = Cls("Tag", ("Name", "string"));
        var order = Cls("Order", ("Tags", "System.Collections.Generic.Dictionary<string, Tag>"));
        var yaml = OpenApiEmitter.Emit(ModelWith(order, tag), new GlobalSettings()).Single().Content;

        Assert.Contains("Tags:", yaml);
        Assert.Contains("additionalProperties:", yaml);
        Assert.Contains("$ref: '#/components/schemas/Tag'", yaml);
    }

    [Fact]
    public void OpenApi_Inheritance_EmitsAllOfWithRefToBase()
    {
        var baseCls = Cls("Entity", ("Id", "int"), ("CreatedAt", "System.DateTime"));
        var child = Cls("Order", ("Customer", "string"));
        child.BaseClassFullName = "Entity";
        var yaml = OpenApiEmitter.Emit(ModelWith(baseCls, child), new GlobalSettings()).Single().Content;

        // Child is expressed as allOf — $ref to base + body with just the new props.
        var orderBlockStart = yaml.IndexOf("    Order:", System.StringComparison.Ordinal);
        var orderBlockEnd = yaml.IndexOf("    Entity:", System.StringComparison.Ordinal);
        // Entity is printed AFTER Order in emission order — just slice from Order to end-of-file.
        var orderBlock = orderBlockEnd < orderBlockStart ? yaml.Substring(orderBlockStart) : yaml.Substring(orderBlockStart, orderBlockEnd - orderBlockStart);

        Assert.Contains("allOf:", orderBlock);
        Assert.Contains("$ref: '#/components/schemas/Entity'", orderBlock);
        Assert.Contains("Customer:", orderBlock);
        // Base properties must NOT appear inline under Order — they're in Entity.
        Assert.DoesNotContain("Id:", orderBlock);
        Assert.DoesNotContain("CreatedAt:", orderBlock);
    }

    [Fact]
    public void TypeScript_Inheritance_EmitsExtendsAndImportsBase()
    {
        var baseCls = Cls("Entity", ("Id", "int"));
        var child = Cls("Order", ("Customer", "string"));
        child.BaseClassFullName = "Entity";
        var files = TypeScriptEmitter.Emit(ModelWith(baseCls, child), new GlobalSettings());
        var orderTs = files.First(f => f.FileName == "Order.ts").Content;

        Assert.Contains("export interface Order extends Entity", orderTs);
        Assert.Contains("import { Entity } from './Entity';", orderTs);
        Assert.Contains("customer: string;", orderTs);
        Assert.DoesNotContain("id: number;", orderTs);   // base's property not duplicated
    }

    [Fact]
    public void OpenApi_BaseNotInModel_InheritanceIsFlattened()
    {
        // When the parser sees a base that's NOT [GenerateTypes]-annotated, it inlines
        // inherited properties directly into the child. The emitter sees no
        // BaseClassFullName lookup hit, so no allOf is emitted.
        var child = Cls("Order", ("Customer", "string"));
        child.Properties.Insert(0, new SchemaProperty { SourceName = "Id", CSharpTypeFullName = "int" });
        // BaseClassFullName points at something not in nameByCSharp.
        child.BaseClassFullName = "SomeLibrary.Entity";
        var yaml = OpenApiEmitter.Emit(ModelWith(child), new GlobalSettings()).Single().Content;

        Assert.DoesNotContain("allOf:", yaml);
        Assert.Contains("Id:", yaml);
        Assert.Contains("Customer:", yaml);
    }
}
