using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Drives <see cref="ConfiguratorParser"/> against synthetic compilations. Each
/// test builds a Roslyn <c>Compilation</c> in memory with a stubbed
/// <c>ITypeGenConfigurator</c>-implementing class and asserts the parser
/// reconstructs the expected global settings / per-type overrides.
///
/// <para>
/// The stubs include the full Abstractions surface (TypeScriptSettings /
/// OpenApiSettings / ITypeGenBuilder signatures) so the DSL resolves semantically
/// — this is closer to the real build than a pure syntax-tree test would be.
/// </para>
/// </summary>
public class ConfiguratorParserTests
{
    [Fact]
    public void NoConfigurator_ReturnsNull()
    {
        var parsed = Parse("public class Empty {}", out var diags);
        Assert.Null(parsed);
        Assert.Empty(diags);
    }

    [Fact]
    public void TypeScriptBlock_SetsGlobalSettings()
    {
        var parsed = Parse("""
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.TypeScript(ts => {
                        ts.OutputDir = "../client/src/api";
                        ts.FileLayout = TypeScriptFileLayout.SingleFile;
                        ts.UseInterfaces = false;
                        ts.PropertyNameStyle = NameStyle.SnakeCase;
                    });
                }
            }
            """, out var diags);

        Assert.Empty(diags);
        Assert.NotNull(parsed);
        Assert.Equal("../client/src/api", parsed!.Settings.TypeScript.OutputDir);
        Assert.Equal(TypeScriptFileLayout.SingleFile, parsed.Settings.TypeScript.FileLayout);
        Assert.False(parsed.Settings.TypeScript.UseInterfaces);
        Assert.Equal(NameStyle.SnakeCase, parsed.Settings.TypeScript.PropertyNameStyle);
    }

    [Fact]
    public void OpenApiBlock_SetsGlobalSettings()
    {
        var parsed = Parse("""
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.OpenApi(oa => {
                        oa.OutputPath = "../api/openapi.yaml";
                        oa.Title = "Order Service";
                        oa.Version = "2.1.0";
                    });
                }
            }
            """, out var diags);

        Assert.Empty(diags);
        Assert.Equal("../api/openapi.yaml", parsed!.Settings.OpenApi.OutputPath);
        Assert.Equal("Order Service", parsed.Settings.OpenApi.Title);
        Assert.Equal("2.1.0", parsed.Settings.OpenApi.Version);
    }

    [Fact]
    public void ForType_CollectsPerTypeOverrides()
    {
        var parsed = Parse("""
            public class Order {}
            public class Junk {}
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>().TsName("OrderDto").OutputDir("gen/orders");
                    b.ForType<Junk>().Ignore();
                }
            }
            """, out var diags);

        Assert.Empty(diags);
        var order = parsed!.PerType["Order"];
        Assert.Equal("OrderDto", order.TsName);
        Assert.Equal("gen/orders", order.OutputDir);
        Assert.False(order.Ignore);

        var junk = parsed.PerType["Junk"];
        Assert.True(junk.Ignore);
    }

    [Fact]
    public void ChainOrderIndependent()
    {
        // Reverse the chain order — result must be identical to the other direction.
        var parsed = Parse("""
            public class Order {}
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>().OutputDir("gen/orders").TsName("OrderDto").OpenApiName("OrderSchema");
                }
            }
            """, out _);
        var o = parsed!.PerType["Order"];
        Assert.Equal("OrderDto", o.TsName);
        Assert.Equal("OrderSchema", o.OpenApiName);
        Assert.Equal("gen/orders", o.OutputDir);
    }

    [Fact]
    public void UnknownMethod_ReportsTG0012()
    {
        Parse("""
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.BogusMethod("oops");
                }
            }
            """, out var diags);
        Assert.Contains(diags, d => d.Id == "TG0012");
    }

    [Fact]
    public void UnknownPerTypeCall_ReportsTG0012()
    {
        var parsed = Parse("""
            public class Order {}
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>().WeirdChain("nope");
                }
            }
            """, out var diags);
        Assert.Contains(diags, d => d.Id == "TG0012");
        // ForType itself should still register the type so later valid calls aren't lost.
        Assert.True(parsed!.PerType.ContainsKey("Order"));
    }

    [Fact]
    public void NonLiteralArg_ReportsTG0013()
    {
        Parse("""
            public class Order {}
            public class Cfg : ITypeGenConfigurator {
                private string _dynamic = System.Environment.MachineName;
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>().TsName(_dynamic);
                }
            }
            """, out var diags);
        Assert.Contains(diags, d => d.Id == "TG0013");
    }

    [Fact]
    public void Property_OverridesAppliedToCorrectProperty()
    {
        var parsed = Parse("""
            public class Order { public string Email { get; set; } public int Id { get; set; } }
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>()
                        .Property(c => c.Email)
                            .TsName("emailAddress")
                            .TsType("string")
                            .OpenApiFormat("email")
                            .OpenApiDescription("Verified email.");
                }
            }
            """, out var diags);

        Assert.Empty(diags);
        var email = parsed!.PerType["Order"].Properties["Email"];
        Assert.Equal("emailAddress", email.TsName);
        Assert.Equal("string", email.TsType);
        Assert.Equal("email", email.OpenApiFormat);
        Assert.Equal("Verified email.", email.OpenApiDescription);
    }

    [Fact]
    public void MultipleProperties_OnSameType_TrackedSeparately()
    {
        var parsed = Parse("""
            public class Order { public string Email { get; set; } public int InternalId { get; set; } }
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>()
                        .Property(c => c.Email).TsName("emailAddress")
                        .Property(c => c.InternalId).Ignore();
                }
            }
            """, out var diags);

        Assert.Empty(diags);
        var props = parsed!.PerType["Order"].Properties;
        Assert.Equal("emailAddress", props["Email"].TsName);
        Assert.True(props["InternalId"].Ignore);
        Assert.Null(props["InternalId"].TsName);  // first prop's TsName must NOT leak to second
    }

    [Fact]
    public void TypeAndPropertyChain_Mixed()
    {
        // Calls before the first .Property(...) target the type; after, target the property.
        var parsed = Parse("""
            public class Order { public string Email { get; set; } }
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>()
                        .TsName("OrderDto")
                        .Property(c => c.Email).TsName("emailAddress");
                }
            }
            """, out var diags);

        Assert.Empty(diags);
        var t = parsed!.PerType["Order"];
        Assert.Equal("OrderDto", t.TsName);
        Assert.Equal("emailAddress", t.Properties["Email"].TsName);
    }

    [Fact]
    public void OpenApiNullable_BoolLiteralAccepted()
    {
        var parsed = Parse("""
            public class Order { public string? Note { get; set; } }
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>().Property(c => c.Note).OpenApiNullable(false);
                }
            }
            """, out var diags);

        Assert.Empty(diags);
        Assert.Equal(false, parsed!.PerType["Order"].Properties["Note"].OpenApiNullable);
    }

    [Fact]
    public void NestedPropertyAccess_NotSupported_ReportsTG0012()
    {
        // c.Inner.Email — only single-member access is allowed.
        var parsed = Parse("""
            public class Inner { public string Email { get; set; } }
            public class Order { public Inner Inner { get; set; } }
            public class Cfg : ITypeGenConfigurator {
                public void Configure(ITypeGenBuilder b) {
                    b.ForType<Order>().Property(c => c.Inner.Email).TsName("x");
                }
            }
            """, out var diags);
        Assert.Contains(diags, d => d.Id == "TG0012");
    }

    [Fact]
    public void MultipleConfigurators_ReportsTG0010()
    {
        Parse("""
            public class A : ITypeGenConfigurator { public void Configure(ITypeGenBuilder b) {} }
            public class B : ITypeGenConfigurator { public void Configure(ITypeGenBuilder b) {} }
            """, out var diags);
        Assert.Contains(diags, d => d.Id == "TG0010");
    }

    // ── test harness ────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles the snippet with minimal Abstractions stubs (matching real signatures)
    /// and runs <see cref="ConfiguratorParser.Parse"/>. We stub Abstractions locally
    /// rather than linking the real DLL because the parser only needs metadata
    /// (interface shape + enum members) — not the full Roslyn-friendly packaging.
    /// </summary>
    private static ConfiguratorParser.Parsed? Parse(string userCode, out IReadOnlyList<Diagnostic> diagnostics)
    {
        const string stubs = """
            using System;
            namespace ZibStack.NET.TypeGen {
                public enum NameStyle { AsIs, CamelCase, SnakeCase, KebabCase, PascalCase }
                public enum TypeScriptFileLayout { FilePerClass, SingleFile }
                public sealed class TypeScriptSettings {
                    public string? OutputDir { get; set; }
                    public string SingleFileName { get; set; } = "models.ts";
                    public TypeScriptFileLayout FileLayout { get; set; }
                    public bool UseInterfaces { get; set; } = true;
                    public NameStyle PropertyNameStyle { get; set; }
                    public NameStyle TypeNameStyle { get; set; }
                    public bool EmitGeneratedBanner { get; set; } = true;
                }
                public sealed class OpenApiSettings {
                    public string OutputPath { get; set; } = "openapi.yaml";
                    public string Title { get; set; } = "API";
                    public string Version { get; set; } = "1.0.0";
                    public string? Description { get; set; }
                    public string OpenApiVersion { get; set; } = "3.0.3";
                }
                public interface ITypeGenBuilder {
                    ITypeGenBuilder TypeScript(Action<TypeScriptSettings> c);
                    ITypeGenBuilder OpenApi(Action<OpenApiSettings> c);
                    ITypeBuilder<T> ForType<T>();
                }
                public interface ITypeBuilder<T> {
                    ITypeBuilder<T> TsName(string n);
                    ITypeBuilder<T> OpenApiName(string n);
                    ITypeBuilder<T> OutputDir(string d);
                    ITypeBuilder<T> Ignore();
                    ITypeBuilder<T> TsIgnore();
                    ITypeBuilder<T> OpenApiIgnore();
                    IPropertyBuilder<T, TProp> Property<TProp>(System.Linq.Expressions.Expression<System.Func<T, TProp>> sel);
                }
                public interface IPropertyBuilder<TClass, TProp> {
                    IPropertyBuilder<TClass, TProp> TsName(string n);
                    IPropertyBuilder<TClass, TProp> TsType(string t);
                    IPropertyBuilder<TClass, TProp> OpenApiName(string n);
                    IPropertyBuilder<TClass, TProp> OpenApiFormat(string f);
                    IPropertyBuilder<TClass, TProp> OpenApiDescription(string d);
                    IPropertyBuilder<TClass, TProp> OpenApiNullable(bool n);
                    IPropertyBuilder<TClass, TProp> Ignore();
                    IPropertyBuilder<TClass, TProp> TsIgnore();
                    IPropertyBuilder<TClass, TProp> OpenApiIgnore();
                    IPropertyBuilder<TClass, TNext> Property<TNext>(System.Linq.Expressions.Expression<System.Func<TClass, TNext>> sel);
                }
                public interface ITypeGenConfigurator { void Configure(ITypeGenBuilder b); }
            }
            """;
        var src = "using ZibStack.NET.TypeGen;\n" + userCode;

        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Action).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Func<>).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "ConfiguratorParserTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(src) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var collected = new List<Diagnostic>();
        var parsed = ConfiguratorParser.Parse(compilation, collected.Add);
        diagnostics = collected;
        return parsed;
    }
}
