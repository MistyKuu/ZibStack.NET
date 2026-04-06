using System;
using System.Collections.Generic;

namespace ZibStack.NET.Result;

/// <summary>
/// Represents a structured error with a code and message.
/// </summary>
public sealed class Error
{
    public string Code { get; }
    public string Message { get; }
    public IReadOnlyList<Error> InnerErrors { get; }

    public Error(string code, string message, IReadOnlyList<Error>? innerErrors = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        InnerErrors = innerErrors ?? Array.Empty<Error>();
    }

    public static Error Validation(string message, IReadOnlyList<Error>? innerErrors = null)
        => new("Validation", message, innerErrors);

    public static Error NotFound(string message)
        => new("NotFound", message);

    public static Error Conflict(string message)
        => new("Conflict", message);

    public static Error Unauthorized(string message)
        => new("Unauthorized", message);

    public static Error Forbidden(string message)
        => new("Forbidden", message);

    public static Error Unexpected(string message)
        => new("Unexpected", message);

    public override string ToString() => $"[{Code}] {Message}";

    public override bool Equals(object? obj)
        => obj is Error other && Code == other.Code && Message == other.Message;

    public override int GetHashCode()
    {
#if NET8_0_OR_GREATER
        return HashCode.Combine(Code, Message);
#else
        unchecked
        {
            return (Code.GetHashCode() * 397) ^ Message.GetHashCode();
        }
#endif
    }
}
