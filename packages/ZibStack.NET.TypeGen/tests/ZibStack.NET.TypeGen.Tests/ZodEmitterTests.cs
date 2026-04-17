using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

// Namespace outside ZibStack.NET.TypeGen.* so the public Abstractions TypeTarget
// doesn't shadow the internal mirror — same reason as TypeScriptEmitterTests.
namespace TypeGenTests;

/// <summary>
/// Unit tests for <see cref="ZodEmitter"/>. Builds a <see cref="SchemaModel"/>
/// directly and asserts on the emitted Zod schema text — no Roslyn round-trip.
/// </summary>
public class ZodEmitterTests
{
    private static SchemaClass Cls(
        string name,
        string outputDir = "out",
        TypeTarget targets = TypeTarget.Zod,
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

    // ── primitives ──────────────────────────────────────────────────────────

    [Fact]
    public void Primitives_MapToExpectedZodExpressions()
    {
        var cls = Cls("Order",
            props: new[]
            {
                ("Id", "int", false),
                ("Name", "string", false),
                ("Active", "bool", false),
                ("Price", "decimal", false),
                ("When", "System.DateTime", false),
                ("Token", "System.Guid", false),
            });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("id: z.number().int()", content);
        Assert.Contains("name: z.string()", content);
        Assert.Contains("active: z.boolean()", content);
        Assert.Contains("price: z.string()", content);  // decimal → string (precision)
        Assert.Contains("when: z.string().datetime()", content);
        Assert.Contains("token: z.string().uuid()", content);
    }

    [Fact]
    public void SchemaConstAndInferredTypeEmittedTogether()
    {
        var cls = Cls("Order", props: new[] { ("Id", "int", false) });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("export const OrderSchema = z.object({", content);
        Assert.Contains("export type Order = z.infer<typeof OrderSchema>;", content);
    }

    [Fact]
    public void EmitInferredTypesFalse_OmitsTypeAlias()
    {
        var cls = Cls("Order", props: new[] { ("Id", "int", false) });
        var settings = new GlobalSettings { Zod = new ZodSettings { EmitInferredTypes = false } };
        var content = ZodEmitter.Emit(ModelWith(cls), settings).Single().Content;

        Assert.Contains("export const OrderSchema", content);
        Assert.DoesNotContain("z.infer", content);
    }

    // ── nullability / read-only ─────────────────────────────────────────────

    [Fact]
    public void Nullable_BecomesNullishModifier()
    {
        var cls = Cls("Order", props: new[] { ("Note", "string", true) });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("note: z.string().nullish()", content);
    }

    [Fact]
    public void ReadOnly_BecomesOptional()
    {
        var cls = Cls("Order");
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Computed",
            CSharpTypeFullName = "int",
            IsNullable = false,
            IsReadOnly = true,
        });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("computed: z.number().int().optional()", content);
    }

    // ── collections ─────────────────────────────────────────────────────────

    [Fact]
    public void ListAndArray_MapToZArray()
    {
        var cls = Cls("Bag",
            props: new[]
            {
                ("Numbers", "int[]", false),
                ("Tags", "List<string>", false),
            });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("numbers: z.array(z.number().int())", content);
        Assert.Contains("tags: z.array(z.string())", content);
    }

    [Fact]
    public void Dictionary_MapsToZRecord()
    {
        var cls = Cls("Map",
            props: new[] { ("Data", "Dictionary<string, int>", false) });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("data: z.record(z.string(), z.number().int())", content);
    }

    // ── validation constraints ──────────────────────────────────────────────

    [Fact]
    public void StringLengthConstraints_AppendMinMax()
    {
        var cls = Cls("Order");
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Sku",
            CSharpTypeFullName = "string",
            MinLength = 3,
            MaxLength = 20,
        });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("sku: z.string().min(3).max(20)", content);
    }

    [Fact]
    public void NumericRange_AppendsGteLte()
    {
        var cls = Cls("Order");
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Qty",
            CSharpTypeFullName = "int",
            Minimum = 1,
            Maximum = 100,
        });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("qty: z.number().int().gte(1).lte(100)", content);
    }

    [Fact]
    public void EmailFormat_BecomesChainedEmail()
    {
        var cls = Cls("Customer");
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Email",
            CSharpTypeFullName = "string",
            OpenApiFormat = "email",
        });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("email: z.string().email()", content);
    }

    [Fact]
    public void Pattern_BecomesRegexLiteral()
    {
        var cls = Cls("Sku");
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Code",
            CSharpTypeFullName = "string",
            Pattern = "^[A-Z]{3}-\\d+$",
        });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("code: z.string().regex(/^[A-Z]{3}-\\d+$/)", content);
    }

    // ── enums ───────────────────────────────────────────────────────────────

    [Fact]
    public void NumericEnum_EmitsLiteralUnion()
    {
        var model = new SchemaModel();
        var en = new SchemaEnum
        {
            CSharpFullName = "Status",
            SourceName = "Status",
            EmittedName = "Status",
            OutputDir = "out",
            Targets = TypeTarget.Zod,
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Done", Value = 1 });
        model.Enums.Add(en);

        var content = ZodEmitter.Emit(model, new GlobalSettings()).Single().Content;
        Assert.Contains("export const StatusSchema = z.union([z.literal(0), z.literal(1)]);", content);
        Assert.Contains("export type Status = z.infer<typeof StatusSchema>;", content);
    }

    [Fact]
    public void StringSerializedEnum_EmitsZEnum()
    {
        var model = new SchemaModel();
        var en = new SchemaEnum
        {
            CSharpFullName = "Status",
            SourceName = "Status",
            EmittedName = "Status",
            OutputDir = "out",
            Targets = TypeTarget.Zod,
            IsStringSerialized = true,
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Done", Value = 1 });
        model.Enums.Add(en);

        var content = ZodEmitter.Emit(model, new GlobalSettings()).Single().Content;
        Assert.Contains("export const StatusSchema = z.enum(['Pending', 'Done']);", content);
    }

    // ── cross-schema references ─────────────────────────────────────────────

    [Fact]
    public void ClassPropertyReferencesSiblingSchema()
    {
        var item = Cls("OrderItem", props: new[] { ("Sku", "string", false) });
        var order = Cls("Order", props: new[] { ("Item", "OrderItem", false) });

        var files = ZodEmitter.Emit(ModelWith(order, item), new GlobalSettings());
        var orderContent = files.Single(f => f.FileName == "Order.schema.ts").Content;

        Assert.Contains("import { OrderItemSchema } from './OrderItem.schema';", orderContent);
        Assert.Contains("item: OrderItemSchema", orderContent);
    }

    [Fact]
    public void TsNameOverride_PreservedVerbatim_NoStyleApplied()
    {
        var cls = Cls("X");
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "UnitPrice",
            CSharpTypeFullName = "int",
            TsNameOverride = "ASD",  // explicit override — should NOT be camelCased to "aSD"
        });
        var content = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("ASD: z.number().int()", content);
        Assert.DoesNotContain("aSD:", content);
    }

    // ── polymorphism ────────────────────────────────────────────────────────

    [Fact]
    public void PolymorphicBase_EmitsDiscriminatedUnion()
    {
        var circle = Cls("Circle", props: new[] { ("Radius", "double", false) });
        circle.PolymorphicDiscriminatorValue = "circle";
        circle.PolymorphicDiscriminatorPropertyOnVariant = "kind";

        var square = Cls("Square", props: new[] { ("Side", "double", false) });
        square.PolymorphicDiscriminatorValue = "square";
        square.PolymorphicDiscriminatorPropertyOnVariant = "kind";

        var shape = Cls("Shape");
        shape.PolymorphicDiscriminator = "kind";
        shape.PolymorphicVariants.Add(new PolymorphicVariant { CSharpFullName = "Circle", DiscriminatorValue = "circle" });
        shape.PolymorphicVariants.Add(new PolymorphicVariant { CSharpFullName = "Square", DiscriminatorValue = "square" });

        var files = ZodEmitter.Emit(ModelWith(shape, circle, square), new GlobalSettings());
        var shapeFile = files.Single(f => f.FileName == "Shape.schema.ts").Content;
        var circleFile = files.Single(f => f.FileName == "Circle.schema.ts").Content;

        Assert.Contains("export const ShapeSchema = z.discriminatedUnion('kind', [", shapeFile);
        Assert.Contains("CircleSchema", shapeFile);
        Assert.Contains("SquareSchema", shapeFile);
        Assert.Contains("kind: z.literal('circle')", circleFile);
    }

    // ── inheritance ─────────────────────────────────────────────────────────

    [Fact]
    public void BaseInModel_EmitsExtendComposition()
    {
        var baseCls = Cls("Entity", props: new[] { ("Id", "int", false) });
        var derived = Cls("Order", props: new[] { ("Customer", "string", false) });
        derived.BaseClassFullName = "Entity";

        var files = ZodEmitter.Emit(ModelWith(baseCls, derived), new GlobalSettings());
        var orderContent = files.Single(f => f.FileName == "Order.schema.ts").Content;

        Assert.Contains("export const OrderSchema = EntitySchema.extend({", orderContent);
        Assert.Contains("customer: z.string()", orderContent);
    }

    // ── settings ────────────────────────────────────────────────────────────

    [Fact]
    public void SingleFileLayout_EmitsOneFileWithAllSchemas()
    {
        var a = Cls("A", props: new[] { ("X", "int", false) });
        var b = Cls("B", props: new[] { ("Y", "string", false) });

        var settings = new GlobalSettings { Zod = new ZodSettings { FileLayout = ZodFileLayout.SingleFile } };
        var files = ZodEmitter.Emit(ModelWith(a, b), settings);

        Assert.Single(files);
        Assert.Equal("schemas.ts", files[0].FileName);
        Assert.Contains("ASchema", files[0].Content);
        Assert.Contains("BSchema", files[0].Content);
    }

    [Fact]
    public void CustomSchemaConstSuffix_Honored()
    {
        var cls = Cls("Order", props: new[] { ("Id", "int", false) });
        var settings = new GlobalSettings { Zod = new ZodSettings { SchemaConstSuffix = "Validator" } };
        var content = ZodEmitter.Emit(ModelWith(cls), settings).Single().Content;

        Assert.Contains("export const OrderValidator = z.object", content);
        Assert.Contains("z.infer<typeof OrderValidator>", content);
    }

    [Fact]
    public void ZodIgnore_Respected()
    {
        var visible = Cls("Public", props: new[] { ("X", "int", false) });
        var hidden = Cls("Secret", props: new[] { ("Y", "int", false) });
        hidden.TsIgnore = true;  // Zod currently shares TsIgnore with TypeScript

        var files = ZodEmitter.Emit(ModelWith(visible, hidden), new GlobalSettings());
        Assert.Single(files);
        Assert.Equal("Public.schema.ts", files[0].FileName);
    }

    [Fact]
    public void SingleFile_PropertyDependencies_EmittedInCorrectOrder()
    {
        var item = Cls("OrderItem", props: new[] { ("Sku", "string", false), ("Qty", "int", false) });
        var order = Cls("Order", props: new[]
        {
            ("Id", "int", false),
            ("Customer", "string", false),
            ("Items", "List<OrderItem>", false),
        });

        var model = new SchemaModel();
        model.Classes.Add(order);
        model.Classes.Add(item);

        var settings = new GlobalSettings
        {
            Zod = { FileLayout = ZodFileLayout.SingleFile, SingleFileName = "schemas.ts" },
        };
        var content = ZodEmitter.Emit(model, settings).Single().Content;

        var itemPos = content.IndexOf("OrderItemSchema = z.object");
        var orderPos = content.IndexOf("OrderSchema = z.object");
        Assert.True(itemPos >= 0, "OrderItemSchema should be in output");
        Assert.True(orderPos >= 0, "OrderSchema should be in output");
        Assert.True(itemPos < orderPos,
            $"OrderItemSchema (pos {itemPos}) must come before OrderSchema (pos {orderPos}).\n\n{content}");
    }

    [Fact]
    public void SingleFile_DeepChain_CorrectOrder()
    {
        var c = Cls("C", props: new[] { ("Value", "int", false) });
        var b = Cls("B", props: new[] { ("Child", "C", false) });
        var a = Cls("A", props: new[] { ("Nested", "B", false) });

        var model = new SchemaModel();
        model.Classes.Add(a);
        model.Classes.Add(b);
        model.Classes.Add(c);

        var settings = new GlobalSettings
        {
            Zod = { FileLayout = ZodFileLayout.SingleFile, SingleFileName = "schemas.ts" },
        };
        var content = ZodEmitter.Emit(model, settings).Single().Content;

        var posC = content.IndexOf("CSchema = z.object");
        var posB = content.IndexOf("BSchema = z.object");
        var posA = content.IndexOf("ASchema = z.object");
        Assert.True(posC < posB, $"C (pos {posC}) must come before B (pos {posB})");
        Assert.True(posB < posA, $"B (pos {posB}) must come before A (pos {posA})");
    }
}
