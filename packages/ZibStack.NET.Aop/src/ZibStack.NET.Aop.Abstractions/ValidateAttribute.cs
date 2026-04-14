using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that validates method parameters before execution using
/// <c>System.ComponentModel.DataAnnotations</c>. Throws <see cref="ArgumentException"/>
/// when validation fails.
///
/// <para>
/// For each parameter that is a complex object, the handler calls
/// <see cref="System.ComponentModel.DataAnnotations.Validator.TryValidateObject"/>
/// with <c>validateAllProperties: true</c>. Primitive/string parameters are skipped.
/// </para>
///
/// <para>
/// If the parameter type implements <c>ICanValidate</c> (from <c>ZibStack.NET.Dto</c>),
/// its <c>Validate()</c> method is called instead.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Validate]
/// public Order CreateOrder(CreateOrderRequest request) { ... }
/// // If request has [Required] Name = null → throws ArgumentException before method runs
/// </code>
/// </example>
[AspectHandler(typeof(ValidateHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ValidateAttribute : AspectAttribute { }
