using System;
using System.Collections.Generic;

namespace ZibStack.NET.Query;

/// <summary>
/// Parses a comma-separated field selection string into a set of lowercase field paths.
/// <para>Example: <c>Name,Level,Team.Name,Team.City</c></para>
/// </summary>
public static class SelectParser
{
    /// <summary>Parses a select string into a set of lowercase field paths.</summary>
    public static HashSet<string> Parse(string? select)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(select))
            return result;

        var parts = select!.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                result.Add(trimmed.ToLowerInvariant());
        }

        return result;
    }

    /// <summary>Returns the set of navigation prefixes present in the field selection (e.g. "team" from "team.name").</summary>
    public static HashSet<string> GetNavigationPrefixes(HashSet<string> fields)
    {
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var dot = field.IndexOf('.');
            if (dot > 0)
                prefixes.Add(field.Substring(0, dot));
        }
        return prefixes;
    }
}
