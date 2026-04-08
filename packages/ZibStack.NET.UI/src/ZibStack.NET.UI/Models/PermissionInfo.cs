using System.Collections.Generic;

namespace ZibStack.NET.UI;

internal sealed class PermissionInfo
{
    public string? ViewPermission { get; set; }
    public Dictionary<string, string> ColumnPermissions { get; } = new Dictionary<string, string>();
    public List<string> DataFilters { get; } = new List<string>();

    public bool HasAny => ViewPermission != null || ColumnPermissions.Count > 0 || DataFilters.Count > 0;
}

internal sealed class ColumnStyleInfo
{
    public string When { get; }
    public string Severity { get; }

    public ColumnStyleInfo(string when, string severity)
    {
        When = when;
        Severity = severity;
    }
}
