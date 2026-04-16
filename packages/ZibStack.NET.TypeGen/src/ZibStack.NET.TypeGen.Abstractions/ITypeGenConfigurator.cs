using System;
using System.Linq.Expressions;

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

    /// <summary>Apply settings to the global <see cref="PythonSettings"/>.</summary>
    ITypeGenBuilder Python(Action<PythonSettings> configure);

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
    /// <summary>
    /// Opt-in: emit code for <typeparamref name="T"/> with the given targets without
    /// requiring <c>[GenerateTypes]</c> on the class. Useful when you can't (or
    /// don't want to) annotate the source — e.g. third-party types or just keeping
    /// model files free of generation markers.
    /// </summary>
    ITypeBuilder<T> WithGeneratedTypes(TypeTarget targets);

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

    /// <summary>
    /// Begin per-property overrides. The selector lambda is parsed at compile time
    /// to extract the property name — only simple member access is supported
    /// (<c>c =&gt; c.Email</c>), no method calls, no nested paths. Returns to the
    /// type builder via the next chained call (the property builder is itself
    /// chainable, so further <c>.Property(...)</c> calls work directly).
    /// </summary>
    IPropertyBuilder<T, TProp> Property<TProp>(Expression<Func<T, TProp>> selector);
}

/// <summary>
/// Per-property fluent overrides. Mirrors the per-property attributes
/// (<see cref="TsNameAttribute"/>, <see cref="TsTypeAttribute"/>,
/// <see cref="OpenApiPropertyAttribute"/>, etc.). Use when you can't or don't
/// want to annotate the source — e.g. DTOs from a referenced library.
/// </summary>
/// <typeparam name="TClass">Owning class.</typeparam>
/// <typeparam name="TProp">Property type — informs IntelliSense, ignored by the parser.</typeparam>
public interface IPropertyBuilder<TClass, TProp>
{
    /// <summary>Equivalent to <c>[TsName(name)]</c> on the property.</summary>
    IPropertyBuilder<TClass, TProp> TsName(string name);

    /// <summary>Equivalent to <c>[TsType(typeExpression)]</c> — opaque TS type literal.</summary>
    IPropertyBuilder<TClass, TProp> TsType(string typeExpression);

    /// <summary>
    /// Equivalent to <c>[TsType(typeExpression, ImportFrom = importFrom)]</c>. The
    /// generator emits an <c>import { … } from '&lt;importFrom&gt;';</c> line at the
    /// top of the file with every PascalCase identifier appearing in
    /// <paramref name="typeExpression"/>. Pass <c>null</c> for <paramref name="importFrom"/>
    /// (or use the single-arg overload) when the type expression doesn't reference an
    /// external symbol.
    /// </summary>
    IPropertyBuilder<TClass, TProp> TsType(string typeExpression, string? importFrom);

    /// <summary>
    /// Generic equivalent of <c>[TsType&lt;T&gt;]</c>. The TS name is read from
    /// <typeparamref name="T"/>'s symbol (refactor-safe); when <typeparamref name="T"/>
    /// is reachable from a <see cref="GenerateTypesAttribute"/> root — directly or via
    /// transitive discovery — the import path is computed from its <c>OutputDir</c>
    /// so no string-literal duplication is needed. Pair with
    /// <see cref="TsType(string, string?)"/> or an explicit <c>ImportFrom</c> on the
    /// attribute form when the target lives outside the generated model.
    /// </summary>
    IPropertyBuilder<TClass, TProp> TsType<T>();

    /// <summary>Equivalent to <c>[OpenApiSchemaName(name)]</c> on the property.</summary>
    IPropertyBuilder<TClass, TProp> OpenApiName(string name);

    /// <summary>
    /// Override the inferred OpenAPI primary type (<c>integer</c>, <c>string</c>,
    /// <c>number</c>, <c>boolean</c>, <c>array</c>, <c>object</c>). Useful for
    /// preserving precision (e.g. force <c>decimal</c> to <c>string</c>) or
    /// representing binary data (<c>byte[]</c> → <c>string</c> + <c>format: byte</c>).
    /// Does NOT change the C# property type, only the emitted schema.
    /// </summary>
    IPropertyBuilder<TClass, TProp> OpenApiType(string openApiType);

    /// <summary>
    /// Emit <c>$ref: '#/components/schemas/{schemaName}'</c> for this property
    /// instead of the inferred shape. Use when the property type is defined
    /// elsewhere in the document (hand-written schema, external reference) and
    /// you want to point at it by name.
    /// </summary>
    IPropertyBuilder<TClass, TProp> OpenApiRef(string schemaName);

    /// <summary>Equivalent to <c>[OpenApiProperty(Format = format)]</c>.</summary>
    IPropertyBuilder<TClass, TProp> OpenApiFormat(string format);

    /// <summary>Equivalent to <c>[OpenApiProperty(Description = description)]</c>.</summary>
    IPropertyBuilder<TClass, TProp> OpenApiDescription(string description);

    /// <summary>Equivalent to <c>[OpenApiProperty(Nullable = nullable)]</c> — overrides NRT inference.</summary>
    IPropertyBuilder<TClass, TProp> OpenApiNullable(bool nullable);

    /// <summary>Skip this property in both TypeScript and OpenAPI output.</summary>
    IPropertyBuilder<TClass, TProp> Ignore();

    /// <summary>Equivalent to <c>[TsIgnore]</c> on the property.</summary>
    IPropertyBuilder<TClass, TProp> TsIgnore();

    /// <summary>Equivalent to <c>[OpenApiIgnore]</c> on the property.</summary>
    IPropertyBuilder<TClass, TProp> OpenApiIgnore();

    /// <summary>Continue with another property on the same owning type.</summary>
    IPropertyBuilder<TClass, TNext> Property<TNext>(Expression<Func<TClass, TNext>> selector);
}
