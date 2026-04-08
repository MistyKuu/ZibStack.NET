using System;
using System.Collections.Generic;

namespace ZibStack.NET.Query;

/// <summary>
/// Parses Gridify-compatible filter strings into <see cref="FilterClause"/> lists.
/// <para>Syntax: <c>Field Op Value</c> separated by <c>,</c> (AND) or <c>|</c> (OR).</para>
/// <para>Operators: <c>= != &lt; &gt; &lt;= &gt;= =* !* ^ !^ $ !$</c></para>
/// <para>Case insensitive suffix: <c>/i</c> (e.g. <c>Name=john/i</c>)</para>
/// </summary>
public static class FilterParser
{
    private static readonly (string Token, FilterOperator Op)[] Operators =
    {
        (">=", FilterOperator.GreaterThanOrEqual),
        ("<=", FilterOperator.LessThanOrEqual),
        ("!=", FilterOperator.NotEquals),
        ("=*", FilterOperator.Contains),
        ("!*", FilterOperator.NotContains),
        ("!^", FilterOperator.NotStartsWith),
        ("!$", FilterOperator.NotEndsWith),
        (">",  FilterOperator.GreaterThan),
        ("<",  FilterOperator.LessThan),
        ("=",  FilterOperator.Equals),
        ("^",  FilterOperator.StartsWith),
        ("$",  FilterOperator.EndsWith),
    };

    /// <summary>
    /// Parses a filter string into a list of clauses.
    /// Currently only supports AND (comma-separated). OR (pipe) and grouping are not yet supported.
    /// </summary>
    public static List<FilterClause> Parse(string? filter)
    {
        var result = new List<FilterClause>();
        if (string.IsNullOrWhiteSpace(filter))
            return result;

        var parts = SplitTopLevel(filter!);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;

            var clause = ParseSingle(trimmed);
            if (clause != null)
                result.Add(clause);
        }

        return result;
    }

    private static FilterClause? ParseSingle(string expression)
    {
        foreach (var (token, op) in Operators)
        {
            var idx = expression.IndexOf(token, StringComparison.Ordinal);
            if (idx <= 0) continue;

            var field = expression.Substring(0, idx).Trim();
            var value = expression.Substring(idx + token.Length).Trim();

            if (field.Length == 0) continue;

            // Check for /i suffix (case insensitive)
            var caseInsensitive = false;
            if (value.EndsWith("/i", StringComparison.Ordinal))
            {
                caseInsensitive = true;
                value = value.Substring(0, value.Length - 2);
            }

            // Unescape Gridify escape sequences
            value = Unescape(value);

            return new FilterClause(field, op, value, caseInsensitive);
        }

        return null;
    }

    /// <summary>Splits on commas that are not inside escaped sequences.</summary>
    private static List<string> SplitTopLevel(string input)
    {
        var parts = new List<string>();
        var start = 0;
        var depth = 0;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (c == '\\' && i + 1 < input.Length)
            {
                i++; // skip escaped char
                continue;
            }

            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(input.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start < input.Length)
            parts.Add(input.Substring(start));

        return parts;
    }

    private static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;

        var chars = new char[value.Length];
        var pos = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                chars[pos++] = value[i];
            }
            else
            {
                chars[pos++] = value[i];
            }
        }
        return new string(chars, 0, pos);
    }
}
