using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;

namespace ZibStack.NET.Query;

/// <summary>
/// Applies filter expressions to <see cref="IQueryable{T}"/> by building LINQ expression trees.
/// Works with EF Core (translates to SQL) and in-memory collections.
/// </summary>
public static class FilterApplier
{
    /// <summary>Applies a single filter clause using a property selector (simple AND chaining).</summary>
    public static IQueryable<T> Apply<T, TProp>(
        IQueryable<T> query,
        Expression<Func<T, TProp>> selector,
        FilterClause clause)
    {
        var predicate = BuildPredicate(selector, clause);
        return predicate != null ? query.Where(predicate) : query;
    }

    /// <summary>
    /// Builds a predicate expression from a property selector and filter clause.
    /// Returns null if the value can't be converted to the property type.
    /// </summary>
    public static Expression<Func<T, bool>>? BuildPredicate<T, TProp>(
        Expression<Func<T, TProp>> selector,
        FilterClause clause)
    {
        var param = selector.Parameters[0];
        var member = selector.Body;
        var propType = typeof(TProp);
        var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;

        if (underlyingType == typeof(string))
            return BuildStringPredicate<T>(param, member, clause);

        return BuildComparablePredicate<T>(param, member, propType, underlyingType, clause);
    }

    /// <summary>
    /// Applies a filter expression tree to a query. The predicateBuilder resolves each leaf clause
    /// to a predicate using the generated per-entity field allowlist.
    /// </summary>
    public static IQueryable<T> ApplyTree<T>(
        IQueryable<T> query,
        FilterExpression? expr,
        Func<FilterClause, Expression<Func<T, bool>>?> predicateBuilder)
    {
        if (expr is null) return query;
        var predicate = BuildTreePredicate(expr, predicateBuilder);
        return predicate is not null ? query.Where(predicate) : query;
    }

    private static Expression<Func<T, bool>>? BuildTreePredicate<T>(
        FilterExpression expr,
        Func<FilterClause, Expression<Func<T, bool>>?> predicateBuilder)
    {
        switch (expr)
        {
            case FilterLeaf leaf:
                return predicateBuilder(leaf.Clause);

            case FilterAnd and:
            {
                var left = BuildTreePredicate(and.Left, predicateBuilder);
                var right = BuildTreePredicate(and.Right, predicateBuilder);
                if (left is null) return right;
                if (right is null) return left;
                return CombineWith(left, right, Expression.AndAlso);
            }

            case FilterOr or:
            {
                var left = BuildTreePredicate(or.Left, predicateBuilder);
                var right = BuildTreePredicate(or.Right, predicateBuilder);
                if (left is null) return right;
                if (right is null) return left;
                return CombineWith(left, right, Expression.OrElse);
            }

            default:
                return null;
        }
    }

    private static Expression<Func<T, bool>> CombineWith<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> combiner)
    {
        var param = left.Parameters[0];
        // Rebind right expression to use the same parameter as left
        var rightBody = new ParameterReplacer(right.Parameters[0], param).Visit(right.Body);
        var combined = combiner(left.Body, rightBody);
        return Expression.Lambda<Func<T, bool>>(combined, param);
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterReplacer(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _from ? _to : base.VisitParameter(node);
    }

    // ─── String predicates ──────────────────────────────────────────

    private static Expression<Func<T, bool>>? BuildStringPredicate<T>(
        ParameterExpression param,
        Expression member,
        FilterClause clause)
    {
        var value = clause.Value;

        var target = clause.CaseInsensitive
            ? Expression.Call(member, nameof(string.ToLower), null)
            : member;
        var val = clause.CaseInsensitive
            ? Expression.Constant(value.ToLower(), typeof(string))
            : Expression.Constant(value, typeof(string));

        var nullCheck = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));

        Expression body;

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
            case FilterOperator.In:
            case FilterOperator.NotIn:
            {
                var items = value.Split(';');
                if (clause.CaseInsensitive)
                    for (var i = 0; i < items.Length; i++) items[i] = items[i].ToLower();
                var listExpr = Expression.Constant(new List<string>(items));
                var containsCall = Expression.Call(listExpr,
                    typeof(List<string>).GetMethod(nameof(List<string>.Contains), new[] { typeof(string) })!,
                    target);
                body = clause.Operator == FilterOperator.In
                    ? Expression.AndAlso(nullCheck, containsCall)
                    : Expression.AndAlso(nullCheck, Expression.Not(containsCall));
                break;
            }
            default:
                body = Expression.AndAlso(nullCheck,
                    BuildComparisonBody(target, val, clause.Operator));
                break;
        }

        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    // ─── Comparable predicates ──────────────────────────────────────

    private static Expression<Func<T, bool>>? BuildComparablePredicate<T>(
        ParameterExpression param,
        Expression member,
        Type propType,
        Type underlyingType,
        FilterClause clause)
    {
        // IN / NOT IN for non-string types
        if (clause.Operator == FilterOperator.In || clause.Operator == FilterOperator.NotIn)
            return BuildInPredicate<T>(param, member, propType, underlyingType, clause);

        object? parsedValue;
        try
        {
            parsedValue = ConvertValue(clause.Value, underlyingType);
        }
        catch
        {
            return null;
        }

        var isNullable = propType != underlyingType;
        Expression target = member;
        Expression valueExpr;

        if (isNullable)
        {
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

    private static Expression<Func<T, bool>>? BuildInPredicate<T>(
        ParameterExpression param,
        Expression member,
        Type propType,
        Type underlyingType,
        FilterClause clause)
    {
        var parts = clause.Value.Split(';');
        var values = new List<object>();
        foreach (var part in parts)
        {
            try { values.Add(ConvertValue(part.Trim(), underlyingType)!); }
            catch { /* skip unparseable values */ }
        }
        if (values.Count == 0) return null;

        // Build: values.Contains(member)
        var listType = typeof(List<>).MakeGenericType(underlyingType);
        var list = Activator.CreateInstance(listType);
        var addMethod = listType.GetMethod("Add")!;
        foreach (var v in values) addMethod.Invoke(list, new[] { v });

        var listExpr = Expression.Constant(list, listType);
        var containsMethod = listType.GetMethod(nameof(List<int>.Contains), new[] { underlyingType })!;

        Expression target = member;
        var isNullable = propType != underlyingType;
        if (isNullable)
            target = Expression.Property(member, "Value");

        var containsCall = Expression.Call(listExpr, containsMethod, target);
        Expression body = clause.Operator == FilterOperator.In ? containsCall : (Expression)Expression.Not(containsCall);

        if (isNullable)
            body = Expression.AndAlso(Expression.Property(member, "HasValue"), body);

        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    // ─── Helpers ────────────────────────────────────────────────────

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
            _ => Expression.Equal(left, right),
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
