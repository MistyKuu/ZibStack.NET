using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace ZibStack.NET.Log;

/// <summary>
/// Interpolated string handler for exceptions that captures both the formatted message
/// and the structured template + properties for structured logging.
/// </summary>
[InterpolatedStringHandler]
public ref struct ZibExceptionInterpolatedStringHandler
{
    private readonly StringBuilder _template;
    private readonly StringBuilder _message;
    private readonly List<KeyValuePair<string, object?>> _properties;

    public ZibExceptionInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _template = new StringBuilder(literalLength + formattedCount * 16);
        _message = new StringBuilder(literalLength + formattedCount * 16);
        _properties = new List<KeyValuePair<string, object?>>(formattedCount);
    }

    public void AppendLiteral(string s)
    {
        _template.Append(s.Replace("{", "{{").Replace("}", "}}"));
        _message.Append(s);
    }

    public void AppendFormatted<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string name = "")
    {
        var sanitized = SanitizeName(name);
        _template.Append('{').Append(sanitized).Append('}');
        _message.Append(value);
        _properties.Add(new KeyValuePair<string, object?>(sanitized, value));
    }

    public void AppendFormatted<T>(
        T value,
        string format,
        [CallerArgumentExpression(nameof(value))] string name = "")
    {
        var sanitized = SanitizeName(name);
        _template.Append('{').Append(sanitized);
        if (!string.IsNullOrEmpty(format))
            _template.Append(':').Append(format);
        _template.Append('}');

        if (value is IFormattable formattable)
            _message.Append(formattable.ToString(format, null));
        else
            _message.Append(value);

        _properties.Add(new KeyValuePair<string, object?>(sanitized, value));
    }

    internal readonly string GetMessage() => _message.ToString();
    internal readonly string GetTemplate() => _template.ToString();
    internal readonly IReadOnlyList<KeyValuePair<string, object?>> GetProperties() => _properties;

    private static string SanitizeName(string expression)
    {
        if (string.IsNullOrEmpty(expression))
            return "_";

        var sb = new StringBuilder(expression.Length);
        bool capitalizeNext = false;

        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = sb.Length > 0;
            }
        }

        return sb.Length > 0 ? sb.ToString() : "_";
    }
}
