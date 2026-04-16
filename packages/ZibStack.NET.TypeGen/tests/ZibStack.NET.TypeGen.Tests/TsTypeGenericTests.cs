using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// <c>[TsType&lt;Foo&gt;]</c> — generic variant of <c>[TsType("Foo")]</c>. The
/// generator reads the generic argument's symbol, uses its emitted TS name, and
/// (when the target is in the current model — either an explicit
/// <c>[GenerateTypes]</c> root or a transitively discovered type) computes the
/// import path from its <c>OutputDir</c> so no string-literal duplication is
/// needed. Explicit <c>ImportFrom</c> still wins for external / hand-written
/// types.
/// </summary>
public class TsTypeGenericTests
{
    [Fact]
    public void GenericTsType_TargetInModel_AutoImport_SameOutputDir()
    {
        var model = ParseAll("""
            using ZibStack.NET.TypeGen;
            using System.Text.Json.Nodes;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                [UseType<Payload>]
                public JsonObject? Element { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Payload
            {
                public string Body { get; set; } = "";
            }
            """);

        var ruleTs = TypeScriptEmitter.Emit(model, new GlobalSettings())
            .First(f => f.FileName == "Rule.ts").Content;

        Assert.Contains("import { Payload } from './Payload';", ruleTs);
        Assert.Contains("element?: Payload;", ruleTs);
    }

    [Fact]
    public void GenericTsType_TargetAutoDiscovered_AutoImport()
    {
        // Payload has NO [GenerateTypes] — but Rule does. [UseType<Payload>] on a
        // property forces TypeGen to treat Payload as reachable; transitive
        // discovery pulls it in and the import gets auto-resolved.
        var model = ParseAll("""
            using ZibStack.NET.TypeGen;
            using System.Text.Json.Nodes;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                [UseType<Payload>]
                public JsonObject? Element { get; set; }
            }

            public class Payload { public string Body { get; set; } = ""; }
            """);

        // Payload auto-pulled as a seed even though it's not referenced by a
        // C#-level property type.
        Assert.Contains(model.Classes, c => c.SourceName == "Payload");
        var ruleTs = TypeScriptEmitter.Emit(model, new GlobalSettings())
            .First(f => f.FileName == "Rule.ts").Content;
        Assert.Contains("import { Payload } from './Payload';", ruleTs);
        Assert.Contains("element?: Payload;", ruleTs);
    }

    [Fact]
    public void GenericTsType_TargetInDifferentOutputDir_RelativePath()
    {
        var model = ParseAll("""
            using ZibStack.NET.TypeGen;
            using System.Text.Json.Nodes;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = "client/src/rules")]
            public class Rule
            {
                [UseType<Payload>]
                public JsonObject? Element { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = "client/src/types")]
            public class Payload { public string Body { get; set; } = ""; }
            """);

        var ruleTs = TypeScriptEmitter.Emit(model, new GlobalSettings())
            .First(f => f.FileName == "Rule.ts").Content;

        Assert.Contains("import { Payload } from '../types/Payload';", ruleTs);
    }

    [Fact]
    public void GenericTsType_ExplicitImportFrom_Wins()
    {
        // Explicit ImportFrom overrides the computed auto-path.
        var model = ParseAll("""
            using ZibStack.NET.TypeGen;
            using System.Text.Json.Nodes;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                [UseType<Payload>(ImportFrom = "./shared/payload")]
                public JsonObject? Element { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Payload { public string Body { get; set; } = ""; }
            """);

        var ruleTs = TypeScriptEmitter.Emit(model, new GlobalSettings())
            .First(f => f.FileName == "Rule.ts").Content;

        Assert.Contains("import { Payload } from './shared/payload';", ruleTs);
    }

    [Fact]
    public void GenericTsType_TargetInBclNamespace_NoImportEmitted()
    {
        // External type — lives in a BCL-ish namespace so IsUserDefined skips it.
        // No seeding, no import. The type expression still comes from the generic
        // argument's name; the user owns the .d.ts / runtime definition themselves.
        var model = ParseAll("""
            using ZibStack.NET.TypeGen;
            using System.Text.Json.Nodes;

            namespace System.Ext { public class ExternalType { public int X { get; set; } } }

            namespace Gen
            {
                [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
                public class Rule
                {
                    [UseType<System.Ext.ExternalType>]
                    public JsonObject? Element { get; set; }
                }
            }
            """);

        var ruleTs = TypeScriptEmitter.Emit(model, new GlobalSettings())
            .First(f => f.FileName == "Rule.ts").Content;

        Assert.Contains("element?: ExternalType;", ruleTs);
        Assert.DoesNotContain("import { ExternalType", ruleTs);
    }

    [Fact]
    public void GenericTsType_TargetWithTsNameOverride_UsesOverriddenName()
    {
        var model = ParseAll("""
            using ZibStack.NET.TypeGen;
            using System.Text.Json.Nodes;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                [UseType<Payload>]
                public JsonObject? Element { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            [TsName("PayloadDto")]
            public class Payload { public string Body { get; set; } = ""; }
            """);

        var ruleTs = TypeScriptEmitter.Emit(model, new GlobalSettings())
            .First(f => f.FileName == "Rule.ts").Content;
        // Both the type expression AND the import use the [TsName] override.
        Assert.Contains("import { PayloadDto } from './PayloadDto';", ruleTs);
        Assert.Contains("element?: PayloadDto;", ruleTs);
    }

    [Fact]
    public void GenericTsType_SeededTarget_WithBaseClass_BaseAlsoAutoSeeded()
    {
        // [UseType<A>] where A inherits from Base. Both A and Base must show up
        // in the emitted set — A via generic seed, Base via the inheritance
        // auto-seed pass. Needs the pipeline to run DiscoverBaseClasses AFTER
        // (or iterated with) SeedGenericTypeTargets; otherwise A's base chain
        // is missed.
        var model = ParseAll("""
            using System.Text.Json.Nodes;
            using ZibStack.NET.TypeGen;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                [UseType<A>]
                public JsonObject? Element { get; set; }
            }

            public class Base { public int Id { get; set; } }
            public class A : Base { public string Extra { get; set; } = ""; }
            """);

        Assert.Contains(model.Classes, c => c.SourceName == "A");
        Assert.Contains(model.Classes, c => c.SourceName == "Base");

        var a = model.Classes.Single(c => c.SourceName == "A");
        Assert.EndsWith(".Base", a.BaseClassFullName);

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings()).ToList();
        Assert.Contains(files, f => f.FileName == "A.ts");
        Assert.Contains(files, f => f.FileName == "Base.ts");
        var aTs = files.First(f => f.FileName == "A.ts").Content;
        Assert.Contains("import { Base } from './Base';", aTs);
        Assert.Contains("export interface A extends Base", aTs);
    }

    [Fact]
    public void GenericTsType_SeededTarget_GoesThroughNestedDiscoveryToo()
    {
        // The T in [UseType<T>] enters the model via SeedGenericTypeTargets
        // *before* DiscoverTransitive runs. That means anything T references
        // (nested classes, enums, collections) rides the same transitive walk
        // as if T had been a normal property type — no special case needed.
        var model = ParseAll("""
            using System.Collections.Generic;
            using System.Text.Json.Nodes;
            using ZibStack.NET.TypeGen;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                // `Element` has no C# type link to Payload — TypeGen would leave
                // it at `unknown` without the generic hint. With [UseType<Payload>]
                // the whole Payload-reachable graph lands in the output.
                [UseType<Payload>]
                public JsonObject? Element { get; set; }
            }

            // Payload has no [GenerateTypes] — pulled in solely by [UseType<Payload>].
            public class Payload
            {
                public string Title { get; set; } = "";
                public Detail Info { get; set; } = new();          // nested class
                public List<Tag> Tags { get; set; } = new();       // collection-wrapped nested
                public Severity Level { get; set; }                // nested enum
            }

            public class Detail { public int Score { get; set; } }
            public class Tag { public string Name { get; set; } = ""; }
            public enum Severity { Low, High }
            """);

        // Seeded via [UseType<Payload>], then DiscoverTransitive walked Payload
        // and pulled in Detail, Tag, Severity by property graph.
        Assert.Contains(model.Classes, c => c.SourceName == "Payload");
        Assert.Contains(model.Classes, c => c.SourceName == "Detail");
        Assert.Contains(model.Classes, c => c.SourceName == "Tag");
        Assert.Contains(model.Enums, e => e.SourceName == "Severity");

        // Everything inherits Rule's Targets + OutputDir (the root that reached it).
        var payload = model.Classes.Single(c => c.SourceName == "Payload");
        Assert.Equal(TypeTarget.TypeScript, payload.Targets);
        Assert.Equal(".", payload.OutputDir);

        // Generated TS: Rule imports Payload, Payload.ts imports its nested types,
        // every file compiles without loose `unknown` references.
        var files = TypeScriptEmitter.Emit(model, new GlobalSettings()).ToList();
        var ruleTs = files.First(f => f.FileName == "Rule.ts").Content;
        Assert.Contains("import { Payload } from './Payload';", ruleTs);
        Assert.Contains("element?: Payload;", ruleTs);

        var payloadTs = files.First(f => f.FileName == "Payload.ts").Content;
        Assert.Contains("import { Detail } from './Detail';", payloadTs);
        Assert.Contains("import { Tag } from './Tag';", payloadTs);
        Assert.Contains("import { Severity } from './Severity';", payloadTs);
        Assert.Contains("title: string;", payloadTs);
        Assert.Contains("info: Detail;", payloadTs);
        Assert.Contains("tags: Tag[];", payloadTs);
        Assert.Contains("level: Severity;", payloadTs);

        // Nested siblings also exist as standalone files.
        Assert.Contains(files, f => f.FileName == "Detail.ts");
        Assert.Contains(files, f => f.FileName == "Tag.ts");
        Assert.Contains(files, f => f.FileName == "Severity.ts");
    }

    [Fact]
    public void GenericTsType_OnEnum_Works()
    {
        var model = ParseAll("""
            using ZibStack.NET.TypeGen;
            namespace Gen;

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public class Rule
            {
                [UseType<Priority>]
                public object? Level { get; set; }
            }

            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
            public enum Priority { Low, Normal, High }
            """);

        var ruleTs = TypeScriptEmitter.Emit(model, new GlobalSettings())
            .First(f => f.FileName == "Rule.ts").Content;
        Assert.Contains("import { Priority } from './Priority';", ruleTs);
        Assert.Contains("level?: Priority;", ruleTs);
    }

    [Fact]
    public void FluentTsTypeGeneric_ReadByConfiguratorParser()
    {
        // Drive the configurator parser directly with a `.UseType<T>()` fluent call
        // and assert that T's FQN lands on the per-property overrides. The late-bind
        // merge into SchemaProperty + path computation happens in TypeGenGenerator,
        // tested end-to-end through the attribute form above — this only verifies
        // the parser's side of the fluent shape.
        var parsed = ParseConfigurator("""
            namespace Ns
            {
                public class Target { }
                public class Owner { public object? El { get; set; } }
                public sealed class Cfg : ITypeGenConfigurator
                {
                    public void Configure(ITypeGenBuilder b)
                    {
                        b.ForType<Owner>()
                            .Property(o => o.El).UseType<Target>();
                    }
                }
            }
            """);
        var owner = parsed.PerType["Ns.Owner"];
        var el = owner.Properties["El"];
        Assert.Equal("Ns.Target", el.TargetTypeCSharpFqn);
    }

    // ── Roslyn harness ──────────────────────────────────────────────────────

    private static SchemaModel ParseAll(string src)
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
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct
                    | System.AttributeTargets.Property | System.AttributeTargets.Field | System.AttributeTargets.Enum)]
                public sealed class TsNameAttribute : System.Attribute {
                    public string Name { get; }
                    public TsNameAttribute(string name) => Name = name;
                }
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
                public sealed class TsTypeAttribute : System.Attribute {
                    public string TypeExpression { get; }
                    public string? ImportFrom { get; set; }
                    public TsTypeAttribute(string typeExpression) => TypeExpression = typeExpression;
                }
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
                public sealed class UseTypeAttribute<T> : System.Attribute {
                    public string? ImportFrom { get; set; }
                }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Text.Json.Nodes.JsonObject).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "TsTypeGenericTest",
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

    private static ConfiguratorParser.Parsed ParseConfigurator(string userCode)
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
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Action).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Func<>).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "TsTypeGenericFluent",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText("using ZibStack.NET.TypeGen;\n" + userCode) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var parsed = ConfiguratorParser.Parse(compilation, _ => { });
        Assert.NotNull(parsed);
        return parsed!;
    }
}
