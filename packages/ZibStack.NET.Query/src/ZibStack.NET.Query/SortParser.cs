using System;
using System.Collections.Generic;

namespace ZibStack.NET.Query;

/// <summary>A single parsed sort directive.</summary>
public sealed class SortClause
{
    /// <summary>Field name to sort by.</summary>
    public string Field { get; }

    /// <summary>True for descending, false for ascending.</summary>
    public bool Descending { get; }

    public SortClause(string field, bool descending)
    {
        Field = field;
        Descending = descending;
    }
}

/// <summary>
/// Parses sort strings. Supports <c>-Field</c> for descending, <c>Field</c> for ascending.
/// Multiple sort fields separated by commas: <c>-Name,Level</c>.
/// Also supports <c>Field desc</c> / <c>Field asc</c> syntax.
/// </summary>
public static class SortParser
{
    public static List<SortClause> Parse(string? sort)
    {
        var result = new List<SortClause>();
        if (string.IsNullOrWhiteSpace(sort))
            return result;

        var parts = sort!.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;

            // Check for "Field desc" / "Field asc" syntax
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx > 0)
            {
                var field = trimmed.Substring(0, spaceIdx).Trim();
                var dir = trimmed.Substring(spaceIdx + 1).Trim();
                var desc = dir.Equals("desc", StringComparison.OrdinalIgnoreCase);
                result.Add(new SortClause(field, desc));
                continue;
            }

            // Check for "-Field" prefix syntax
            if (trimmed[0] == '-')
            {
                result.Add(new SortClause(trimmed.Substring(1), descending: true));
            }
            else
            {
                result.Add(new SortClause(trimmed, descending: false));
            }
        }

        return result;
    }
}
