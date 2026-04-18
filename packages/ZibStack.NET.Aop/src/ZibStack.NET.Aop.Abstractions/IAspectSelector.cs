using System;
using System.Linq.Expressions;

namespace ZibStack.NET.Aop;

/// <summary>
/// Fluent selector for bulk-applying aspects to classes and methods that match
/// compile-time predicates. Used inside <see cref="IAopConfigurator.Configure"/>:
/// <code>
/// b.Apply&lt;CacheAttribute&gt;(to => to
///     .Namespace("MyApp.Services")
///     .Implementing&lt;IRepository&gt;()
///     .PublicMethods()
/// , c => c.DurationSeconds = 120);
/// </code>
///
/// <para>
/// The generator parses selector chains at compile time — predicates are
/// expression trees evaluated against Roslyn symbols, not runtime reflection.
/// All selectors are AND-combined (intersection).
/// </para>
/// </summary>
public interface IAspectSelector
{
    /// <summary>
    /// Match classes whose namespace starts with <paramref name="prefix"/>.
    /// <c>.Namespace("MyApp.Services")</c> matches <c>MyApp.Services</c>,
    /// <c>MyApp.Services.Orders</c>, etc.
    /// </summary>
    IAspectSelector Namespace(string prefix);

    /// <summary>Match classes that implement <typeparamref name="T"/>.</summary>
    IAspectSelector Implementing<T>();

    /// <summary>Match classes that derive from <typeparamref name="T"/>.</summary>
    IAspectSelector DerivedFrom<T>();

    /// <summary>
    /// Filter classes by predicate. Available properties on the predicate parameter:
    /// <c>Name</c> (string), <c>IsAbstract</c> (bool), <c>IsSealed</c> (bool).
    /// <code>.ClassesWhere(c => c.Name.StartsWith("Order"))</code>
    /// </summary>
    IAspectSelector ClassesWhere(Expression<Func<ClassInfo, bool>> predicate);

    /// <summary>
    /// Filter methods by predicate. Available properties on the predicate parameter:
    /// <c>Name</c> (string), <c>IsAsync</c> (bool), <c>IsPublic</c> (bool),
    /// <c>IsStatic</c> (bool).
    /// <code>.MethodsWhere(m => m.Name.StartsWith("Get") &amp;&amp; m.IsAsync)</code>
    /// </summary>
    IAspectSelector MethodsWhere(Expression<Func<MethodInfo, bool>> predicate);

    /// <summary>Shortcut for <c>.MethodsWhere(m => m.IsPublic)</c>.</summary>
    IAspectSelector PublicMethods();

    /// <summary>Exclude a specific class from matching.</summary>
    IAspectSelector Except<T>();
}

/// <summary>
/// Compile-time proxy for class metadata. Properties are evaluated against
/// Roslyn <c>INamedTypeSymbol</c> during generation — not at runtime.
/// </summary>
public sealed class ClassInfo
{
    /// <summary>Simple class name (e.g. <c>"OrderService"</c>).</summary>
    public string Name { get; set; } = "";
    public bool IsAbstract { get; set; }
    public bool IsSealed { get; set; }
}

/// <summary>
/// Compile-time proxy for method metadata. Properties are evaluated against
/// Roslyn <c>IMethodSymbol</c> during generation — not at runtime.
/// </summary>
public sealed class MethodInfo
{
    /// <summary>Method name (e.g. <c>"GetOrderAsync"</c>).</summary>
    public string Name { get; set; } = "";
    public bool IsAsync { get; set; }
    public bool IsPublic { get; set; }
    public bool IsStatic { get; set; }
}
