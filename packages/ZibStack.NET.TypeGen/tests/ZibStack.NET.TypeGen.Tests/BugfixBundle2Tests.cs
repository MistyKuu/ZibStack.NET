using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Three regressions reported together:
/// <list type="number">
/// <item><c>[TsType&lt;T&gt;]</c> failing to seed T / emit its import when T
/// lives in a referenced assembly OR lacks <c>[GenerateTypes]</c>.</item>
/// <item>Inheritance — base class properties disappearing from the emitted
/// schema for derived classes when the base isn't annotated.</item>
/// <item>Nullable reference property (<c>Foo? X</c>) not pulling in <c>Foo</c>
/// via transitive discovery.</item>
/// </list>
/// </summary>
public class BugfixBundle2Tests
{
    [Fact]
    public void NullableReferenceProperty_TriggersTransitiveDiscovery()
    {
        var model = Parse("""
            using ZibStack.NET.TypeGen;
            namespace Ns;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Root
            {
                public Foo? MaybeFoo { get; set; }      // nullable reference
            }

            public class Foo { public string Name { get; set; } = ""; }
            """);

        Assert.Contains(model.Classes, c => c.SourceName == "Foo");
    }

    [Fact]
    public void InheritedProperties_FromNonAnnotatedBase_StillEmitted()
    {
        // Base has no [GenerateTypes]. Derived has one. Base's properties must
        // still appear on Derived's emitted schema — either inlined (today's
        // behavior) or as `extends` if transitive discovery later hoists the
        // base into the model. Either way the properties can't just disappear.
        var model = Parse("""
            using ZibStack.NET.TypeGen;
            namespace Ns;

            public class Base
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Derived : Base
            {
                public string Email { get; set; } = "";
            }
            """);

        var derived = model.Classes.Single(c => c.SourceName == "Derived");
        var propNames = derived.Properties.Select(p => p.SourceName).ToList();
        Assert.Contains("Id", propNames);      // inlined from Base
        Assert.Contains("Name", propNames);    // inlined from Base
        Assert.Contains("Email", propNames);   // declared on Derived
    }

    [Fact]
    public void UntranslatableProperty_SurfacesAsDiagnostic_ViaValidator()
    {
        // Hand-build the model — the validator is what we're testing, not the
        // full generator pipeline. JsonObject isn't in the model and has no
        // override — TG0002 must fire for both TS and OpenAPI.
        var cls = new SchemaClass
        {
            CSharpFullName = "Ns.Root", SourceName = "Root", EmittedName = "Root",
            OutputDir = ".", Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
        };
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Opaque",
            CSharpTypeFullName = "System.Text.Json.Nodes.JsonObject",
        });
        var model = new SchemaModel();
        model.Classes.Add(cls);

        var diags = RunValidatorOnly(model);
        Assert.True(diags.Any(d => d.Id == "TG0002" && d.GetMessage().Contains("Opaque")),
            $"Expected TG0002 for Opaque. Got: [{string.Join(", ", diags.Select(d => d.Id + ":" + d.GetMessage()))}]");
    }

    [Fact]
    public void TranslatableProperty_NoDiagnostic()
    {
        // Root's properties all resolve — no spurious TG0002.
        var root = new SchemaClass
        {
            CSharpFullName = "Ns.Root", SourceName = "Root", EmittedName = "Root",
            OutputDir = ".", Targets = TypeTarget.TypeScript,
        };
        root.Properties.Add(new SchemaProperty { SourceName = "Id", CSharpTypeFullName = "int" });
        root.Properties.Add(new SchemaProperty { SourceName = "Name", CSharpTypeFullName = "string" });
        root.Properties.Add(new SchemaProperty { SourceName = "Items", CSharpTypeFullName = "System.Collections.Generic.List<Ns.Item>" });

        var item = new SchemaClass
        {
            CSharpFullName = "Ns.Item", SourceName = "Item", EmittedName = "Item",
            OutputDir = ".", Targets = TypeTarget.TypeScript,
        };
        item.Properties.Add(new SchemaProperty { SourceName = "Qty", CSharpTypeFullName = "int" });

        var model = new SchemaModel();
        model.Classes.Add(root);
        model.Classes.Add(item);

        var diags = RunValidatorOnly(model);
        Assert.Empty(diags.Where(d => d.Id == "TG0002"));
    }

    [Fact]
    public void TsIgnoreProperty_SuppressesDiagnostic()
    {
        // Property has an untranslatable type but is marked [TsIgnore] —
        // emitter won't render it, so validator shouldn't complain about it.
        var cls = new SchemaClass
        {
            CSharpFullName = "Ns.R", SourceName = "R", EmittedName = "R",
            OutputDir = ".", Targets = TypeTarget.TypeScript,
        };
        cls.Properties.Add(new SchemaProperty
        {
            SourceName = "Internal",
            CSharpTypeFullName = "System.Text.Json.Nodes.JsonObject",
            TsIgnore = true,
        });
        var model = new SchemaModel();
        model.Classes.Add(cls);

        var diags = RunValidatorOnly(model);
        Assert.Empty(diags.Where(d => d.Id == "TG0002"));
    }

    private static System.Collections.Generic.List<Diagnostic> RunValidatorOnly(SchemaModel model)
    {
        var diags = new System.Collections.Generic.List<Diagnostic>();
        TypeGenGenerator.ValidateTranslatableProperties(model, diags.Add);
        return diags;
    }

    [Fact]
    public void GenericTsType_TargetInReferencedAssembly_StillSeedsAndImports()
    {
        // Split into two compilations: the referenced lib defines Payload, the
        // consumer references it and uses [TsType<Payload>]. Today's IsUserDefined
        // check rejects external-assembly types — user explicitly writing
        // [TsType<T>] should override that heuristic (they asked for T by name).
        var libRefs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };
        var lib = CSharpCompilation.Create(
            "ExternalLib",
            new[] { CSharpSyntaxTree.ParseText("namespace Lib { public class Payload { public string Body { get; set; } = \"\"; } }") },
            libRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var libStream = new System.IO.MemoryStream();
        var emitResult = lib.Emit(libStream);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));
        libStream.Position = 0;

        var model = Parse("""
            using ZibStack.NET.TypeGen;
            using Lib;
            namespace Consumer;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                [TsType<Payload>]
                public object? El { get; set; }
            }
            """, extraRefs: new[] { MetadataReference.CreateFromImage(libStream.ToArray()) });

        Assert.Contains(model.Classes, c => c.SourceName == "Payload");
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private static SchemaModel Parse(string src, MetadataReference[]? extraRefs = null)
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
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
                public sealed class TsTypeAttribute : System.Attribute {
                    public string TypeExpression { get; }
                    public string? ImportFrom { get; set; }
                    public TsTypeAttribute(string typeExpression) => TypeExpression = typeExpression;
                }
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
                public sealed class TsTypeAttribute<T> : System.Attribute { public string? ImportFrom { get; set; } }
            }
            """;
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
        };
        if (extraRefs is not null) refs.AddRange(extraRefs);

        var compilation = CSharpCompilation.Create(
            "BugBundle2Test",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(src) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var model = new SchemaModel();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var sm = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax cd
                    && sm.GetDeclaredSymbol(cd) is INamedTypeSymbol c
                    && SchemaParser.HasGenerateTypes(c))
                {
                    var cls = SchemaParser.ParseClass(c);
                    if (cls is not null) model.Classes.Add(cls);
                }
                else if (node is Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax ed
                    && sm.GetDeclaredSymbol(ed) is INamedTypeSymbol e
                    && SchemaParser.HasGenerateTypes(e))
                {
                    var en = SchemaParser.ParseEnum(e);
                    if (en is not null) model.Enums.Add(en);
                }
            }
        }
        SchemaParser.SeedGenericTsTypeTargets(model, compilation);
        SchemaParser.DiscoverTransitive(model, compilation);
        SchemaParser.ResolveGenericTsTypeReferences(model);
        return model;
    }
}
