using System.Collections.Generic;

namespace ZibStack.NET.UI;

internal sealed class FormClassInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string HintName { get; }
    public string FormName { get; }
    public string Layout { get; }
    public bool IsRecord { get; }
    public List<FormFieldInfo> Fields { get; }
    public List<FormGroupInfo> Groups { get; }
    public string? ApiUrl { get; set; }
    public string? KeyProperty { get; set; }
    public bool IsPartial { get; set; }
    public List<RelationInfo> Relations { get; } = new List<RelationInfo>();

    public FormClassInfo(
        string className,
        string? ns,
        string hintName,
        string formName,
        string layout,
        bool isRecord,
        List<FormFieldInfo> fields,
        List<FormGroupInfo> groups)
    {
        ClassName = className;
        Namespace = ns;
        HintName = hintName;
        FormName = formName;
        Layout = layout;
        IsRecord = isRecord;
        Fields = fields;
        Groups = groups;
    }
}
