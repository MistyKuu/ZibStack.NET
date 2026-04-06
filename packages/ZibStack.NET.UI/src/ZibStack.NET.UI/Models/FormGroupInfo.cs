namespace ZibStack.NET.UI;

internal sealed class FormGroupInfo
{
    public string Name { get; }
    public string? Label { get; }
    public int Order { get; }

    public FormGroupInfo(string name, string? label, int order)
    {
        Name = name;
        Label = label;
        Order = order;
    }
}
