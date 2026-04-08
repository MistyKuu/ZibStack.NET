namespace ZibStack.NET.Query;

/// <summary>A single parsed filter condition.</summary>
public sealed class FilterClause
{
    /// <summary>Field name (e.g. "Name", "Level", "Team.Name").</summary>
    public string Field { get; }

    /// <summary>Comparison operator.</summary>
    public FilterOperator Operator { get; }

    /// <summary>Raw value string to compare against.</summary>
    public string Value { get; }

    /// <summary>Whether the comparison is case-insensitive (/i suffix).</summary>
    public bool CaseInsensitive { get; }

    public FilterClause(string field, FilterOperator op, string value, bool caseInsensitive = false)
    {
        Field = field;
        Operator = op;
        Value = value;
        CaseInsensitive = caseInsensitive;
    }
}
