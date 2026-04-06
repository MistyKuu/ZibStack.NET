namespace ZibStack.NET.UI;

internal sealed class RowActionInfo
{
    public string Name { get; }
    public string Label { get; }
    public string? Icon { get; }
    public string Endpoint { get; }
    public string Method { get; }
    public string? Confirmation { get; }
    public string? Permission { get; }

    public RowActionInfo(string name, string label, string? icon, string endpoint, string method, string? confirmation, string? permission)
    {
        Name = name;
        Label = label;
        Icon = icon;
        Endpoint = endpoint;
        Method = method;
        Confirmation = confirmation;
        Permission = permission;
    }
}

internal sealed class ToolbarActionInfo
{
    public string Name { get; }
    public string Label { get; }
    public string? Icon { get; }
    public string Endpoint { get; }
    public string Method { get; }
    public string? Confirmation { get; }
    public string? Permission { get; }
    public string SelectionMode { get; }

    public ToolbarActionInfo(string name, string label, string? icon, string endpoint, string method, string? confirmation, string? permission, string selectionMode)
    {
        Name = name;
        Label = label;
        Icon = icon;
        Endpoint = endpoint;
        Method = method;
        Confirmation = confirmation;
        Permission = permission;
        SelectionMode = selectionMode;
    }
}
