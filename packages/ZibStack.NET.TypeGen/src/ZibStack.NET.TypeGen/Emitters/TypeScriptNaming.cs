using System.Linq;
using System.Text;

namespace ZibStack.NET.TypeGen.Generator;

internal static class TypeScriptNaming
{
    public static void ApplyEmittedNames(SchemaModel model, TypeScriptSettings ts, TypeTarget targetMask)
    {
        foreach (var cls in model.Classes)
        {
            if (cls.TsIgnore || (cls.Targets & targetMask) == 0) continue;
            var name = ResolveTypeName(cls.SourceName, cls.TsNameOverride, ts);
            cls.EmittedName = name;
            cls.TypeScriptEmittedName = name;
        }

        foreach (var en in model.Enums)
        {
            if (en.TsIgnore || (en.Targets & targetMask) == 0) continue;
            var name = ResolveTypeName(en.SourceName, en.TsNameOverride, ts);
            en.EmittedName = name;
            en.TypeScriptEmittedName = name;
        }
    }

    public static string ResolveTypeName(string source, string? overrideName, TypeScriptSettings ts)
    {
        if (overrideName != null) return overrideName;

        var n = source;
        foreach (var suffix in ts.StripSuffixes.OrderByDescending(s => s.Length))
        {
            if (n.EndsWith(suffix, System.StringComparison.Ordinal) && n.Length > suffix.Length)
            {
                n = n.Substring(0, n.Length - suffix.Length);
                break;
            }
        }

        return ApplyNameStyle(n, ts.TypeNameStyle);
    }

    public static string ApplyNameStyle(string name, NameStyle style)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return style switch
        {
            NameStyle.AsIs => name,
            NameStyle.CamelCase => char.ToLowerInvariant(name[0]) + name.Substring(1),
            NameStyle.PascalCase => char.ToUpperInvariant(name[0]) + name.Substring(1),
            NameStyle.SnakeCase => ToSeparated(name, '_'),
            _ => name,
        };
    }

    public static bool TryMapPrimitive(string cSharpType, out string typeScriptType)
    {
        typeScriptType = cSharpType.Trim().TrimEnd('?') switch
        {
            "string" or "char" or "System.String" or "System.Char" => "string",
            "bool" or "System.Boolean" => "boolean",
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong"
                or "float" or "double" or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
                or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
                or "System.Single" or "System.Double" => "number",
            "decimal" or "System.Decimal" => "string",
            "System.Guid" or "Guid" => "string",
            "System.DateTime" or "DateTime" or "System.DateTimeOffset" or "DateTimeOffset" => "string",
            "System.DateOnly" or "DateOnly" or "System.TimeOnly" or "TimeOnly" or "System.TimeSpan" or "TimeSpan" => "string",
            "object" or "System.Object" => "unknown",
            "void" or "System.Void" => "void",
            _ => "",
        };

        return typeScriptType.Length > 0;
    }

    private static string ToSeparated(string name, char sep)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (!char.IsLetterOrDigit(ch))
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != sep) sb.Append(sep);
                continue;
            }

            if (i > 0 && char.IsUpper(ch) && sb.Length > 0 && sb[sb.Length - 1] != sep)
                sb.Append(sep);

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString().Trim(sep);
    }
}
