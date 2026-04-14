using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="ValidateAttribute"/>. Validates complex object parameters
/// using <see cref="System.ComponentModel.DataAnnotations.Validator"/> before method execution.
/// </summary>
public sealed class ValidateHandler : IAspectHandler
{
    /// <inheritdoc />
    public void OnBefore(AspectContext context)
    {
        foreach (var param in context.Parameters)
        {
            if (param.Value is null) continue;

            var type = param.Value.GetType();

            // Skip primitives, strings, value types — only validate complex objects
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)
                || type == typeof(DateTime) || type == typeof(DateTimeOffset)
                || type == typeof(Guid) || type.IsEnum)
                continue;

            var results = new List<ValidationResult>();
            var validationContext = new ValidationContext(param.Value);

            if (!Validator.TryValidateObject(param.Value, validationContext, results, validateAllProperties: true))
            {
                var errors = new List<string>();
                foreach (var result in results)
                {
                    if (result.ErrorMessage is not null)
                        errors.Add(result.ErrorMessage);
                }

                throw new ArgumentException(
                    $"Validation failed for parameter '{param.Name}' in {context.ClassName}.{context.MethodName}: {string.Join("; ", errors)}",
                    param.Name);
            }
        }
    }

    /// <inheritdoc />
    public void OnAfter(AspectContext context) { }

    /// <inheritdoc />
    public void OnException(AspectContext context, Exception exception) { }
}
