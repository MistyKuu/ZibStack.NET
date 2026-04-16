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
    public void InheritedProperties_FromNonAnnotatedBase_BaseAutoSeededAsOwnSchema()
    {
        // Base has no [GenerateTypes]. Derived has one. Preserve-structure semantics:
        // Base gets auto-seeded as its own schema (via DiscoverBaseClasses) and
        // Derived extends it. Each class owns only its declared members — the
        // inheritance shape mirrors the C# hierarchy 1:1.
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
        Assert.Equal(new[] { "Email" }, derived.Properties.Select(p => p.SourceName));
        Assert.EndsWith(".Base", derived.BaseClassFullName);

        var baseCls = model.Classes.Single(c => c.SourceName == "Base");
        var baseProps = baseCls.Properties.Select(p => p.SourceName).ToList();
        Assert.Contains("Id", baseProps);
        Assert.Contains("Name", baseProps);
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
        Assert.DoesNotContain(diags, d => d.Id == "TG0002");
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
        Assert.DoesNotContain(diags, d => d.Id == "TG0002");
    }

    private static System.Collections.Generic.List<Diagnostic> RunValidatorOnly(SchemaModel model)
    {
        var diags = new System.Collections.Generic.List<Diagnostic>();
        TypeGenGenerator.ValidateTranslatableProperties(model, diags.Add);
        return diags;
    }

    [Fact]
    public void FluentWithGeneratedTypes_PlusPropertyTsTypeGeneric_Works()
    {
        // End-to-end: the parent class is discovered *only* via the fluent
        // `.WithGeneratedTypes(...)` path (no [GenerateTypes] attribute). Its
        // property carries a fluent `.UseType<Payload>()` — Payload also has no
        // attribute. The pipeline must:
        //   1. discover the parent via WithGeneratedTypes,
        //   2. merge the per-property TargetTypeCSharpFqn via fluent,
        //   3. seed Payload via SeedGenericTypeTargets,
        //   4. rewrite the property to point at Payload + auto-path import.
        var userCode = """
            using ZibStack.NET.TypeGen;
            namespace Ns;

            public class Root
            {
                public object? El { get; set; }
            }

            public class Payload
            {
                public string Body { get; set; } = "";
            }

            public sealed class Cfg : ITypeGenConfigurator
            {
                public void Configure(ITypeGenBuilder b)
                {
                    b.ForType<Root>()
                        .WithGeneratedTypes(TypeTarget.TypeScript)
                        .OutputDir(".")
                        .Property(r => r.El).UseType<Payload>();
                }
            }
            """;
        var (model, compilation) = RunFullPipeline(userCode);

        // Root got emitted by fluent-only discovery
        Assert.Contains(model.Classes, c => c.SourceName == "Root");
        // Payload got seeded by SeedGenericTypeTargets from the .UseType<Payload>()
        Assert.Contains(model.Classes, c => c.SourceName == "Payload");

        // Emitted TS has both the import AND the property type pointing at Payload
        var files = TypeScriptEmitter.Emit(model, new GlobalSettings());
        var rootTs = files.First(f => f.FileName == "Root.ts").Content;
        Assert.Contains("import { Payload } from './Payload';", rootTs);
        Assert.Contains("el?: Payload;", rootTs);
    }

    [Fact]
    public void Fluent_PropertyTsTypeGeneric_OnTransitivelyDiscoveredClass_SeedsTarget()
    {
        // User repro: XD is the only fluent root. A is transitively discovered
        // (via XD.As : List<A>). Fluent sets `.Property(a => a.Element).UseType<Side>()`
        // on A — Side must land in the model so Element renders as Side (not unknown).
        var userCode = """
            using ZibStack.NET.TypeGen;
            using System.Collections.Generic;
            using System.Text.Json;
            using System.Text.Json.Nodes;
            namespace Ns;

            public record XD
            {
                public JsonObject? Element { get; init; }
                public List<A> As { get; set; } = new();
                public List<B> Bs { get; set; } = new();
            }

            public abstract record C
            {
                public string Hehe1 { get; set; } = "";
            }

            public record B : C;

            public record A : B
            {
                public string Test { get; set; } = "";
                public JsonElement Element { get; set; }
            }

            public record Side
            {
                public string A { get; set; } = "";
            }

            public sealed class Cfg : ITypeGenConfigurator
            {
                public void Configure(ITypeGenBuilder b)
                {
                    b.ForType<XD>().WithGeneratedTypes(TypeTarget.TypeScript).OutputDir(".");
                    b.ForType<A>().Property(x => x.Element).UseType<Side>();
                }
            }
            """;
        var (model, _) = RunFullPipeline(userCode);

        // Side must have been seeded by SeedGenericTypeTargets after fluent
        // merge made A.Element.TargetTypeCSharpFqn = Side's FQN.
        Assert.Contains(model.Classes, c => c.SourceName == "Side");

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings()).ToList();
        Assert.Contains(files, f => f.FileName == "Side.ts");
        var aTs = files.First(f => f.FileName == "A.ts").Content;
        Assert.Contains("import { Side } from './Side';", aTs);
        Assert.Contains("Side", aTs);
        Assert.Matches(@"[Ee]lement:\s*Side", aTs);
    }

    [Fact]
    public void AllFluent_WithGeneratedTypes_PropertyTsTypeGeneric_ChainsNested()
    {
        // 100% fluent: no [GenerateTypes] anywhere, no [TsType] attribute. Everything
        // flows through the configurator — Root registered via WithGeneratedTypes,
        // property El typed via fluent .UseType<Dupa>(), and Dupa's own nested graph
        // (Detail, Tag) rides transitive discovery the same way a regular property
        // type would.
        var userCode = """
            using ZibStack.NET.TypeGen;
            using System.Collections.Generic;
            namespace Ns;

            public class Root
            {
                public object? El { get; set; }
            }

            public class Dupa
            {
                public string Title { get; set; } = "";
                public Detail Info { get; set; } = new();
                public List<Tag> Tags { get; set; } = new();
            }

            public class Detail { public int Score { get; set; } }
            public class Tag { public string Name { get; set; } = ""; }

            public sealed class Cfg : ITypeGenConfigurator
            {
                public void Configure(ITypeGenBuilder b)
                {
                    b.ForType<Root>()
                        .WithGeneratedTypes(TypeTarget.TypeScript)
                        .OutputDir(".")
                        .Property(r => r.El).UseType<Dupa>();
                }
            }
            """;
        var (model, compilation) = RunFullPipeline(userCode);

        // Full chain pulled in, end to end, via fluent only.
        Assert.Contains(model.Classes, c => c.SourceName == "Root");
        Assert.Contains(model.Classes, c => c.SourceName == "Dupa");
        Assert.Contains(model.Classes, c => c.SourceName == "Detail");
        Assert.Contains(model.Classes, c => c.SourceName == "Tag");

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings());
        var rootTs = files.First(f => f.FileName == "Root.ts").Content;
        Assert.Contains("import { Dupa } from './Dupa';", rootTs);
        Assert.Contains("el?: Dupa;", rootTs);

        var dupaTs = files.First(f => f.FileName == "Dupa.ts").Content;
        Assert.Contains("import { Detail } from './Detail';", dupaTs);
        Assert.Contains("import { Tag } from './Tag';", dupaTs);
        Assert.Contains("title: string;", dupaTs);
        Assert.Contains("info: Detail;", dupaTs);
        Assert.Contains("tags: Tag[];", dupaTs);

        // Nested siblings standalone too.
        Assert.Contains(files, f => f.FileName == "Detail.ts");
        Assert.Contains(files, f => f.FileName == "Tag.ts");
    }

    private static (SchemaModel model, CSharpCompilation compilation) RunFullPipeline(string userCode)
    {
        const string stubs = """
            using System;
            namespace ZibStack.NET.TypeGen {
                [System.Flags] public enum TypeTarget { None = 0, TypeScript = 1, OpenApi = 2, Python = 4 }
                public enum NameStyle { AsIs, CamelCase, SnakeCase, PascalCase }
                public enum TypeScriptFileLayout { FilePerClass, SingleFile }
                public sealed class TypeScriptSettings {
                    public string? OutputDir { get; set; } public string SingleFileName { get; set; } = "m.ts";
                    public TypeScriptFileLayout FileLayout { get; set; } public bool UseInterfaces { get; set; } = true;
                    public NameStyle PropertyNameStyle { get; set; } public NameStyle TypeNameStyle { get; set; }
                    public bool EmitGeneratedBanner { get; set; } = true;
                }
                public sealed class OpenApiSettings {
                    public string OutputPath { get; set; } = "o.yaml"; public string Title { get; set; } = "API";
                    public string Version { get; set; } = "1.0.0"; public string? Description { get; set; }
                    public string OpenApiVersion { get; set; } = "3.0.3";
                }
                public interface ITypeGenBuilder {
                    ITypeGenBuilder TypeScript(Action<TypeScriptSettings> c);
                    ITypeGenBuilder OpenApi(Action<OpenApiSettings> c);
                    ITypeBuilder<T> ForType<T>();
                }
                public interface ITypeBuilder<T> {
                    ITypeBuilder<T> WithGeneratedTypes(TypeTarget targets);
                    ITypeBuilder<T> TsName(string n); ITypeBuilder<T> OpenApiName(string n);
                    ITypeBuilder<T> OutputDir(string d); ITypeBuilder<T> Ignore();
                    ITypeBuilder<T> TsIgnore(); ITypeBuilder<T> OpenApiIgnore();
                    IPropertyBuilder<T, TProp> Property<TProp>(System.Linq.Expressions.Expression<System.Func<T, TProp>> sel);
                }
                public interface IPropertyBuilder<TClass, TProp> {
                    IPropertyBuilder<TClass, TProp> TsName(string n);
                    IPropertyBuilder<TClass, TProp> TsType(string t);
                    IPropertyBuilder<TClass, TProp> TsType(string t, string? importFrom);
                    IPropertyBuilder<TClass, TProp> UseType<TTarget>();
                    IPropertyBuilder<TClass, TProp> OpenApiName(string n);
                    IPropertyBuilder<TClass, TProp> OpenApiType(string t); IPropertyBuilder<TClass, TProp> OpenApiRef(string s);
                    IPropertyBuilder<TClass, TProp> OpenApiFormat(string f); IPropertyBuilder<TClass, TProp> OpenApiDescription(string d);
                    IPropertyBuilder<TClass, TProp> OpenApiNullable(bool n);
                    IPropertyBuilder<TClass, TProp> Ignore(); IPropertyBuilder<TClass, TProp> TsIgnore();
                    IPropertyBuilder<TClass, TProp> OpenApiIgnore();
                    IPropertyBuilder<TClass, TNext> Property<TNext>(System.Linq.Expressions.Expression<System.Func<TClass, TNext>> sel);
                }
                public interface ITypeGenConfigurator { void Configure(ITypeGenBuilder b); }
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
                public sealed class GenerateTypesAttribute : Attribute {
                    public TypeTarget Targets { get; set; }
                    public string? OutputDir { get; set; }
                }
                [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
                public sealed class UseTypeAttribute<T> : Attribute { public string? ImportFrom { get; set; } }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Action).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Func<>).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "FluentPipelineTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(userCode) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Replicate the generator's pipeline step by step.
        var model = new SchemaModel();
        var config = ConfiguratorParser.Parse(compilation, _ => { });

        // Fluent-only discovery (no [GenerateTypes] attributes in user src).
        if (config is not null)
        {
            foreach (var kvp in config.PerType)
            {
                if (kvp.Value.FluentTargets is not int fluentTargets) continue;
                var sym = compilation.GetTypeByMetadataName(kvp.Key);
                if (sym is null) continue;
                var dir = !string.IsNullOrEmpty(kvp.Value.OutputDir) ? kvp.Value.OutputDir!
                        : !string.IsNullOrEmpty(config.Settings.TypeScript.OutputDir) ? config.Settings.TypeScript.OutputDir!
                        : ".";
                var aux = SchemaParser.ParseAuxiliaryClass(sym, (TypeTarget)fluentTargets, dir);
                if (aux is null) continue;
                // Merge fluent-per-property overrides (including TargetTypeCSharpFqn).
                if (config.PerType.TryGetValue(aux.CSharpFullName, out var pto))
                    foreach (var prop in aux.Properties)
                        if (pto.Properties.TryGetValue(prop.SourceName, out var po))
                        {
                            prop.TsNameOverride ??= po.TsName;
                            prop.TsTypeOverride ??= po.TsType;
                            prop.TsImportFrom ??= po.TsImportFrom;
                            prop.TargetTypeCSharpFqn ??= po.TargetTypeCSharpFqn;
                        }
                model.Classes.Add(aux);
            }
        }

        int lastCount, guard = 0;
        do
        {
            lastCount = model.Classes.Count + model.Enums.Count;
            var clsBefore = model.Classes.Count;
            SchemaParser.SeedGenericTypeTargets(model, compilation);
            SchemaParser.DiscoverBaseClasses(model, compilation);
            SchemaParser.DiscoverTransitive(model, compilation);
            // Mirror the generator: newly added classes go through a fluent
            // merge pass so config-side per-property overrides (TsType<T>,
            // TsName, Ignore, …) reach their SchemaProperty before the next
            // SeedGeneric iteration reads TargetTypeCSharpFqn.
            for (int i = clsBefore; i < model.Classes.Count; i++)
                MergeFluentInto(model.Classes[i], config);
        } while (model.Classes.Count + model.Enums.Count > lastCount && ++guard < 16);
        SchemaParser.ResolveGenericTypeReferences(model);
        return (model, compilation);
    }

    private static void MergeFluentInto(SchemaClass cls, ConfiguratorParser.Parsed? config)
    {
        if (config is null) return;
        if (!config.PerType.TryGetValue(cls.CSharpFullName, out var pto)) return;
        cls.TsNameOverride ??= pto.TsName;
        cls.OpenApiNameOverride ??= pto.OpenApiName;
        if (pto.Ignore) { cls.TsIgnore = true; cls.OpenApiIgnore = true; }
        cls.TsIgnore |= pto.TsIgnore;
        cls.OpenApiIgnore |= pto.OpenApiIgnore;
        foreach (var prop in cls.Properties)
            if (pto.Properties.TryGetValue(prop.SourceName, out var po))
            {
                prop.TsNameOverride ??= po.TsName;
                prop.TsTypeOverride ??= po.TsType;
                prop.TsImportFrom ??= po.TsImportFrom;
                prop.TargetTypeCSharpFqn ??= po.TargetTypeCSharpFqn;
                if (po.Ignore) { prop.TsIgnore = true; prop.OpenApiIgnore = true; }
                prop.TsIgnore |= po.TsIgnore;
                prop.OpenApiIgnore |= po.OpenApiIgnore;
            }
    }

    [Fact]
    public void GenericTsType_TargetInReferencedAssembly_StillSeedsAndImports()
    {
        // Split into two compilations: the referenced lib defines Payload, the
        // consumer references it and uses [UseType<Payload>]. Today's IsUserDefined
        // check rejects external-assembly types — user explicitly writing
        // [UseType<T>] should override that heuristic (they asked for T by name).
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
                [UseType<Payload>]
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
                public sealed class UseTypeAttribute<T> : System.Attribute { public string? ImportFrom { get; set; } }
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
        int lastCount, guard = 0;
        do
        {
            lastCount = model.Classes.Count + model.Enums.Count;
            SchemaParser.SeedGenericTypeTargets(model, compilation);
            SchemaParser.DiscoverBaseClasses(model, compilation);
            SchemaParser.DiscoverTransitive(model, compilation);
        } while (model.Classes.Count + model.Enums.Count > lastCount && ++guard < 16);
        SchemaParser.ResolveGenericTypeReferences(model);
        return model;
    }
}
