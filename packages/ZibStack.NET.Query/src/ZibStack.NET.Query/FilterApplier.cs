using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace ZibStack.NET.Query;

/// <summary>
/// Applies a <see cref="FilterClause"/> to an <see cref="IQueryable{T}"/> by building LINQ expressions.
/// The generated per-entity code calls this with a typed property selector.
/// </summary>
public static class FilterApplier
{
    /// <summary>
    /// Applies a filter clause to a query using a property selector.
    /// Works with EF Core (translates to SQL) and in-memory collections.
    /// </summary>
    public static IQueryable<T> Apply<T, TProp>(
        IQueryable<T> query,
        Expression<Func<T, TProp>> selector,
        FilterClause clause)
    {
        var predicate = BuildPredicate(selector, clause);
        return predicate != null ? query.Where(predicate) : query;
    }

    private static Expression<Func<T, bool>>? BuildPredicate<T, TProp>(
        Expression<Func<T, TProp>> selector,
        FilterClause clause)
    {
        var param = selector.Parameters[0];
        var member = selector.Body;
        var propType = typeof(TProp);
        var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;

        // String operations
        if (underlyingType == typeof(string))
            return BuildStringPredicate<T>(param, member, clause);

        // Numeric / comparable operations
        return BuildComparablePredicate<T>(param, member, propType, underlyingType, clause);
    }

    private static Expression<Func<T, bool>>? BuildStringPredicate<T>(
        ParameterExpression param,
        Expression member,
        FilterClause clause)
    {
        var value = clause.Value;
        var valueExpr = Expression.Constant(value, typeof(string));

        Expression body;

        // For case-insensitive, use ToLower() on both sides
        var target = clause.CaseInsensitive
            ? Expression.Call(member, nameof(string.ToLower), null)
            : member;
        var val = clause.CaseInsensitive
            ? Expression.Constant(value.ToLower(), typeof(string))
            : valueExpr;

        // Handle null check for nullable strings
        var nullCheck = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));

        switch (clause.Operator)
        {
            case FilterOperator.Equals:
                body = Expression.Equal(target, val);
                break;
            case FilterOperator.NotEquals:
                body = Expression.NotEqual(target, val);
                break;
            case FilterOperator.Contains:
                body = Expression.AndAlso(nullCheck,
                    Expression.Call(target, nameof(string.Contains), null, val));
                break;
            case FilterOperator.NotContains:
                body = Expression.AndAlso(nullCheck,
                    Expression.Not(Expression.Call(target, nameof(string.Contains), null, val)));
                break;
            case FilterOperator.StartsWith:
                body = Expression.AndAlso(nullCheck,
                    Expression.Call(target, nameof(string.StartsWith), null, val));
                break;
            case FilterOperator.NotStartsWith:
                body = Expression.AndAlso(nullCheck,
                    Expression.Not(Expression.Call(target, nameof(string.StartsWith), null, val)));
                break;
            case FilterOperator.EndsWith:
                body = Expression.AndAlso(nullCheck,
                    Expression.Call(target, nameof(string.EndsWith), null, val));
                break;
            case FilterOperator.NotEndsWith:
                body = Expression.AndAlso(nullCheck,
                    Expression.Not(Expression.Call(target, nameof(string.EndsWith), null, val)));
                break;
            default:
                // Comparison operators on strings: lexicographic
                body = Expression.AndAlso(nullCheck,
                    BuildComparisonBody(target, val, clause.Operator));
                break;
        }

        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    private static Expression<Func<T, bool>>? BuildComparablePredicate<T>(
        ParameterExpression param,
        Expression member,
        Type propType,
        Type underlyingType,
        FilterClause clause)
    {
        object? parsedValue;
        try
        {
            parsedValue = ConvertValue(clause.Value, underlyingType);
        }
        catch
        {
            return null; // can't convert → skip this filter
        }

        var isNullable = propType != underlyingType;
        Expression target = member;
        Expression valueExpr;

        if (isNullable)
        {
            // For nullable types, access .Value and add HasValue check
            var hasValue = Expression.Property(member, "HasValue");
            target = Expression.Property(member, "Value");
            valueExpr = Expression.Constant(parsedValue, underlyingType);

            var comparison = BuildComparisonBody(target, valueExpr, clause.Operator);
            var body = Expression.AndAlso(hasValue, comparison);
            return Expression.Lambda<Func<T, bool>>(body, param);
        }
        else
        {
            valueExpr = Expression.Constant(parsedValue, underlyingType);
            var body = BuildComparisonBody(target, valueExpr, clause.Operator);
            return Expression.Lambda<Func<T, bool>>(body, param);
        }
    }

    private static Expression BuildComparisonBody(Expression left, Expression right, FilterOperator op)
    {
        return op switch
        {
            FilterOperator.Equals => Expression.Equal(left, right),
            FilterOperator.NotEquals => Expression.NotEqual(left, right),
            FilterOperator.GreaterThan => Expression.GreaterThan(left, right),
            FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, right),
            FilterOperator.LessThan => Expression.LessThan(left, right),
            FilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(left, right),
            _ => Expression.Equal(left, right), // fallback
        };
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(long)) return long.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(decimal)) return decimal.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(bool)) return bool.Parse(value);
        if (targetType == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(Guid)) return Guid.Parse(value);
        if (targetType.IsEnum) return Enum.Parse(targetType, value, ignoreCase: true);

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }
}
