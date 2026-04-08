using System.Collections.Generic;

namespace ZibStack.NET.Core;

internal sealed class UtilsPropertyInfo
{
    public string PropertyName { get; }
    public string DisplayTypeName { get; }
    public bool IsNullable { get; }
    public bool IsValueType { get; }
    public List<string> ValidationAttributes { get; }

    public UtilsPropertyInfo(string propertyName, string displayTypeName, bool isNullable, bool isValueType, List<string>? validationAttributes = null)
    {
        PropertyName = propertyName;
        DisplayTypeName = displayTypeName;
        IsNullable = isNullable;
        IsValueType = isValueType;
        ValidationAttributes = validationAttributes ?? new List<string>();
    }
}
