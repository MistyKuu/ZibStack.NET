using System;

namespace ZibStack.NET.TypeGen;

/// <summary>
/// Overrides the emitted TypeScript identifier for a class or property. Without
/// this attribute, the C# name is used verbatim (or transformed by
/// <see cref="TypeScriptSettings.PropertyNameStyle"/> for properties).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property |
                AttributeTargets.Field | AttributeTargets.Enum,
    Inherited = false, AllowMultiple = false)]
public sealed class TsNameAttribute : Attribute
{
    public string Name { get; }
    public TsNameAttribute(string name) => Name = name;
}

/// <summary>
/// Overrides the emitted TypeScript type expression for a property. Pass any valid
/// TypeScript type — primitive, union, generic, branded type, etc. The generator
/// emits this verbatim, no quoting.
/// </summary>
/// <example>
/// <code>
/// [TsType("string")]
/// public Guid Id { get; set; }
///
/// [TsType("'pending' | 'shipped' | 'delivered'")]
/// public OrderStatus Status { get; set; }
///
/// // External named type — TypeGen emits an `import { AutomationRulePayload }
/// // from './types/automation-rule-payload';` line at the top of the file.
/// [TsType("AutomationRulePayload", ImportFrom = "./types/automation-rule-payload")]
/// public JsonObject? Element { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false, AllowMultiple = false)]
public sealed class TsTypeAttribute : Attribute
{
    public string TypeExpression { get; }

    /// <summary>
    /// Optional. Module specifier to import the named symbol(s) appearing in
    /// <see cref="TypeExpression"/> from. PascalCase identifiers in the expression
    /// are treated as imported names; primitives like <c>string</c>, <c>number</c>,
    /// literal unions, etc. are left alone. Leave <c>null</c> when the type expression
    /// doesn't reference an external symbol (e.g. <c>"string"</c>, <c>"'a' | 'b'"</c>).
    /// </summary>
    public string? ImportFrom { get; set; }

    public TsTypeAttribute(string typeExpression) => TypeExpression = typeExpression;
}

/// <summary>
/// Generic variant of <see cref="TsTypeAttribute"/>. The TypeScript type name is
/// read from <typeparamref name="T"/>'s symbol at compile time (refactor-safe —
/// renaming <typeparamref name="T"/> updates this reference automatically). When
/// <typeparamref name="T"/> is reachable from a <see cref="GenerateTypesAttribute"/>
/// root (either directly annotated or pulled in by transitive discovery), the
/// import path is computed from its emitted <c>OutputDir</c> — no string literal
/// needed. When the target lives outside the model (BCL, NuGet package,
/// hand-written <c>.d.ts</c>) set <see cref="ImportFrom"/> explicitly or the
/// symbol name is emitted without an <c>import</c> line.
/// </summary>
/// <typeparam name="T">Any type — its simple name and (optionally) its
/// <see cref="TsNameAttribute"/> override drive the emitted TS expression.</typeparam>
/// <example>
/// <code>
/// // Zero string literals — compile-time safe.
/// [TsType&lt;AutomationRulePayload&gt;]
/// public JsonObject? Element { get; set; }
///
/// // Explicit path wins when the target isn't in the generated model.
/// [TsType&lt;ExternalLib.Widget&gt;(ImportFrom = "@acme/widgets")]
/// public object? Widget { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false, AllowMultiple = false)]
public sealed class TsTypeAttribute<T> : Attribute
{
    /// <summary>See <see cref="TsTypeAttribute.ImportFrom"/>.</summary>
    public string? ImportFrom { get; set; }
}

/// <summary>
/// Excludes the annotated class or property from TypeScript output. The C# member
/// stays untouched; only the emitted .ts file omits it. Use for properties that
/// are server-only details (audit columns, internal flags, etc.).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property |
                AttributeTargets.Field | AttributeTargets.Enum,
    Inherited = false, AllowMultiple = false)]
public sealed class TsIgnoreAttribute : Attribute
{
}
