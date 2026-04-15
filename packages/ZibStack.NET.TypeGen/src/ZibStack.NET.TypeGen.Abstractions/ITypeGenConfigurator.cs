using System;

namespace ZibStack.NET.TypeGen;

/// <summary>
/// Project-level configuration for the TypeGen generator. Implement once per
/// project — the generator auto-discovers the implementing type and parses the
/// <see cref="Configure"/> method body at compile time (nothing is actually
/// invoked at runtime).
///
/// <para>
/// <b>Compile-time DSL only.</b> The generator walks the syntax tree of
/// <see cref="Configure"/> and reconstructs the fluent calls. That means:
/// </para>
/// <list type="bullet">
///   <item>Method arguments must be literal expressions, constants, or
///         <c>nameof(...)</c> — anything dynamic (reflection, conditionals, loops,
///         field reads) is invisible to the generator.</item>
///   <item>Lambda bodies for <c>TypeScript(ts =&gt; ...)</c> / <c>OpenApi(oa =&gt; ...)</c>
///         must be simple property assignments (<c>ts.Foo = "bar"</c>).</item>
///   <item>Multiple <see cref="ITypeGenConfigurator"/> implementations in one project
///         is an error (diagnostic <c>TG0010</c>).</item>
/// </list>
///
/// <para>
/// Precedence, lowest to highest: defaults → <c>TypeScript</c>/<c>OpenApi</c>
/// global blocks → <c>ForType&lt;T&gt;()</c> per-type fluent → attributes on the
/// class/property (<see cref="TsNameAttribute"/>, <see cref="OpenApiPropertyAttribute"/>,
/// etc.). Each layer overrides the previous.
/// </para>
/// </summary>
/// <example>
/// <code>
/// public sealed class MyTypeGenConfig : ITypeGenConfigurator
/// {
///     public void Configure(ITypeGenBuilder b)
///     {
///         b.TypeScript(ts =&gt;
///         {
///             ts.OutputDir = "../client/src/api";
///             ts.FileLayout = TypeScriptFileLayout.SingleFile;
///             ts.PropertyNameStyle = NameStyle.CamelCase;
///         });
///
///         b.OpenApi(oa =&gt;
///         {
///             oa.OutputPath = "../api/openapi.yaml";
///             oa.Title = "Order Service";
///             oa.Version = "2.1.0";
///         });
///
///         b.ForType&lt;Order&gt;()
///             .TsName("OrderDto")
///             .OutputDir("generated/orders");
///
///         b.ForType&lt;InternalAudit&gt;().Ignore();
///     }
/// }
/// </code>
/// </example>
public interface ITypeGenConfigurator
{
    /// <summary>
    /// Build the TypeGen configuration. Called by the generator at compile time —
    /// body is parsed as a DSL, never actually executed.
    /// </summary>
    void Configure(ITypeGenBuilder builder);
}

/// <summary>
/// Fluent builder for project-level TypeGen configuration. Method signatures only —
/// bodies are never invoked. The generator reads the syntax of chained calls from
/// <see cref="ITypeGenConfigurator.Configure"/> to derive effective settings.
/// </summary>
public interface ITypeGenBuilder
{
    /// <summary>Apply settings to the global <see cref="TypeScriptSettings"/>.</summary>
    ITypeGenBuilder TypeScript(Action<TypeScriptSettings> configure);

    /// <summary>Apply settings to the global <see cref="OpenApiSettings"/>.</summary>
    ITypeGenBuilder OpenApi(Action<OpenApiSettings> configure);

    /// <summary>
    /// Begin per-type overrides for <typeparamref name="T"/>. Overrides take
    /// precedence over the <c>TypeScript</c>/<c>OpenApi</c> global blocks but lose
    /// to per-class / per-property attributes.
    /// </summary>
    ITypeBuilder<T> ForType<T>();
}

/// <summary>
/// Per-type fluent overrides. Mirrors the per-class attributes
/// (<see cref="TsNameAttribute"/>, <see cref="OpenApiSchemaNameAttribute"/>,
/// <see cref="GenerateTypesAttribute.OutputDir"/>, <see cref="TsIgnoreAttribute"/>,
/// <see cref="OpenApiIgnoreAttribute"/>) for cases where you want to configure a
/// DTO without touching its source (e.g. a DTO from a referenced library).
/// </summary>
public interface ITypeBuilder<T>
{
    /// <summary>Equivalent to <c>[TsName(name)]</c> on the class.</summary>
    ITypeBuilder<T> TsName(string name);

    /// <summary>Equivalent to <c>[OpenApiSchemaName(name)]</c> on the class.</summary>
    ITypeBuilder<T> OpenApiName(string name);

    /// <summary>Equivalent to <c>[GenerateTypes(OutputDir = dir)]</c> on the class.</summary>
    ITypeBuilder<T> OutputDir(string dir);

    /// <summary>Skip this type in both TypeScript and OpenAPI output.</summary>
    ITypeBuilder<T> Ignore();

    /// <summary>Equivalent to <c>[TsIgnore]</c> on the class.</summary>
    ITypeBuilder<T> TsIgnore();

    /// <summary>Equivalent to <c>[OpenApiIgnore]</c> on the class.</summary>
    ITypeBuilder<T> OpenApiIgnore();
}
