using System.Collections.Generic;

namespace ZibStack.NET.Utils;

internal sealed class UtilsPropertyInfo
{
    public string PropertyName { get; }
    public string JsonName { get; }
    public string DisplayTypeName { get; }
    public bool IsNullable { get; }
    public bool IsValueType { get; }
    public List<string> ValidationAttributes { get; }

    public UtilsPropertyInfo(string propertyName, string jsonName, string displayTypeName, bool isNullable, bool isValueType, List<string>? validationAttributes = null)
    {
        PropertyName = propertyName;
        JsonName = jsonName;
        DisplayTypeName = displayTypeName;
        IsNullable = isNullable;
        IsValueType = isValueType;
        ValidationAttributes = validationAttributes ?? new List<string>();
    }
}
