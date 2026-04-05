using System.Runtime.CompilerServices;
using System.Text;

namespace ZibStack.NET.Log;

/// <summary>
/// Interpolated string handler that captures template and arguments separately
/// for structured logging. Uses <see cref="CallerArgumentExpressionAttribute"/> to
/// preserve variable names as structured property names.
/// </summary>
[InterpolatedStringHandler]
public ref struct ZibLogInterpolatedStringHandler
{
    private readonly StringBuilder _template;
    private readonly object?[] _args;
    private int _argIndex;

    public ZibLogInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _template = new StringBuilder(literalLength + formattedCount * 16);
        _args = new object?[formattedCount];
        _argIndex = 0;
    }

    public void AppendLiteral(string s)
    {
        _template.Append(s.Replace("{", "{{").Replace("}", "}}"));
    }

    public void AppendFormatted<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string name = "")
    {
        _template.Append('{').Append(SanitizeName(name)).Append('}');
        _args[_argIndex++] = value;
    }

    public void AppendFormatted<T>(
        T value,
        string format,
        [CallerArgumentExpression(nameof(value))] string name = "")
    {
        _template.Append('{').Append(SanitizeName(name));
        if (!string.IsNullOrEmpty(format))
            _template.Append(':').Append(format);
        _template.Append('}');
        _args[_argIndex++] = value;
    }

    internal readonly string GetTemplate() => _template.ToString();
    internal readonly object?[] GetArgs() => _args;

    /// <summary>
    /// Sanitizes CallerArgumentExpression to a valid message template property name.
    /// "user.Name" → "userName", "items[0]" → "items0", "GetId()" → "GetId"
    /// </summary>
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
