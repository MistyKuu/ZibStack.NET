using System;
using System.Collections.Generic;

namespace ZibStack.NET.Query;

/// <summary>
/// Parses filter strings into expression trees.
/// <para>Syntax: <c>Field Op Value</c> separated by <c>,</c> (AND) or <c>|</c> (OR).</para>
/// <para>Grouping: <c>(expr1 | expr2), expr3</c></para>
/// <para>Operators: <c>= != &lt; &gt; &lt;= &gt;= =* !* ^ !^ $ !$ =in= =out=</c></para>
/// <para>Case insensitive suffix: <c>/i</c></para>
/// </summary>
public static class FilterParser
{
    private static readonly (string Token, FilterOperator Op)[] Operators =
    {
        ("=in=",  FilterOperator.In),
        ("=out=", FilterOperator.NotIn),
        (">=",    FilterOperator.GreaterThanOrEqual),
        ("<=",    FilterOperator.LessThanOrEqual),
        ("!=",    FilterOperator.NotEquals),
        ("=*",    FilterOperator.Contains),
        ("!*",    FilterOperator.NotContains),
        ("!^",    FilterOperator.NotStartsWith),
        ("!$",    FilterOperator.NotEndsWith),
        (">",     FilterOperator.GreaterThan),
        ("<",     FilterOperator.LessThan),
        ("=",     FilterOperator.Equals),
        ("^",     FilterOperator.StartsWith),
        ("$",     FilterOperator.EndsWith),
    };

    /// <summary>Parses a filter string into a flat list of AND clauses (backward-compatible).</summary>
    public static List<FilterClause> Parse(string? filter)
    {
        var result = new List<FilterClause>();
        if (string.IsNullOrWhiteSpace(filter))
            return result;

        var expr = ParseExpression(filter!);
        CollectClauses(expr, result);
        return result;
    }

    /// <summary>Parses a filter string into an expression tree supporting AND, OR, and grouping.</summary>
    public static FilterExpression? ParseExpression(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var tokens = Tokenize(filter!);
        var pos = 0;
        return ParseOr(tokens, ref pos);
    }

    // ─── Recursive descent parser ───────────────────────────────────

    // Precedence (low→high): OR (|) → AND (,) → atom (leaf or grouped)

    private static FilterExpression? ParseOr(List<Token> tokens, ref int pos)
    {
        var left = ParseAnd(tokens, ref pos);
        if (left is null) return null;

        while (pos < tokens.Count && tokens[pos].Type == TokenType.Or)
        {
            pos++; // consume |
            var right = ParseAnd(tokens, ref pos);
            if (right is null) break;
            left = new FilterOr(left, right);
        }

        return left;
    }

    private static FilterExpression? ParseAnd(List<Token> tokens, ref int pos)
    {
        var left = ParseAtom(tokens, ref pos);
        if (left is null) return null;

        while (pos < tokens.Count && tokens[pos].Type == TokenType.And)
        {
            pos++; // consume ,
            var right = ParseAtom(tokens, ref pos);
            if (right is null) break;
            left = new FilterAnd(left, right);
        }

        return left;
    }

    private static FilterExpression? ParseAtom(List<Token> tokens, ref int pos)
    {
        if (pos >= tokens.Count) return null;

        if (tokens[pos].Type == TokenType.OpenParen)
        {
            pos++; // consume (
            var expr = ParseOr(tokens, ref pos);
            if (pos < tokens.Count && tokens[pos].Type == TokenType.CloseParen)
                pos++; // consume )
            return expr;
        }

        if (tokens[pos].Type == TokenType.Expression)
        {
            var clause = ParseSingle(tokens[pos].Value);
            pos++;
            return clause is not null ? new FilterLeaf(clause) : null;
        }

        return null;
    }

    // ─── Tokenizer ──────────────────────────────────────────────────

    private enum TokenType { Expression, And, Or, OpenParen, CloseParen }

    private sealed class Token
    {
        public TokenType Type { get; }
        public string Value { get; }
        public Token(TokenType type, string value = "")
        {
            Type = type;
            Value = value;
        }
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var start = 0;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (c == '\\' && i + 1 < input.Length)
            {
                i++; // skip escaped char
                continue;
            }

            switch (c)
            {
                case '(':
                    FlushExpression(tokens, input, start, i);
                    tokens.Add(new Token(TokenType.OpenParen));
                    start = i + 1;
                    break;
                case ')':
                    FlushExpression(tokens, input, start, i);
                    tokens.Add(new Token(TokenType.CloseParen));
                    start = i + 1;
                    break;
                case ',':
                    FlushExpression(tokens, input, start, i);
                    tokens.Add(new Token(TokenType.And));
                    start = i + 1;
                    break;
                case '|':
                    FlushExpression(tokens, input, start, i);
                    tokens.Add(new Token(TokenType.Or));
                    start = i + 1;
                    break;
            }
        }

        FlushExpression(tokens, input, start, input.Length);
        return tokens;
    }

    private static void FlushExpression(List<Token> tokens, string input, int start, int end)
    {
        if (end > start)
        {
            var text = input.Substring(start, end - start).Trim();
            if (text.Length > 0)
                tokens.Add(new Token(TokenType.Expression, text));
        }
    }

    // ─── Single clause parser ───────────────────────────────────────

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

            value = Unescape(value);
            return new FilterClause(field, op, value, caseInsensitive);
        }

        return null;
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

    private static void CollectClauses(FilterExpression? expr, List<FilterClause> result)
    {
        switch (expr)
        {
            case FilterLeaf leaf:
                result.Add(leaf.Clause);
                break;
            case FilterAnd and:
                CollectClauses(and.Left, result);
                CollectClauses(and.Right, result);
                break;
            case FilterOr or:
                CollectClauses(or.Left, result);
                CollectClauses(or.Right, result);
                break;
        }
    }
}
