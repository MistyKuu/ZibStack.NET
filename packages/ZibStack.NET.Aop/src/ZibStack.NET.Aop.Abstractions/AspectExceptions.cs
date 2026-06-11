using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Thrown by <see cref="AuthorizeHandler"/> when authorization fails. Derives from
/// <see cref="UnauthorizedAccessException"/> so existing catch blocks keep working.
/// On methods returning <c>ZibStack.NET.Result.Result</c>/<c>Result&lt;T&gt;</c> the generated
/// interceptor converts this into a failed Result with <c>Error.Unauthorized</c> instead of throwing.
/// </summary>
public sealed class AspectAuthorizationException : UnauthorizedAccessException
{
    public AspectAuthorizationException(string message) : base(message) { }
}

/// <summary>
/// Thrown by <see cref="ValidateHandler"/> when parameter validation fails. Derives from
/// <see cref="ArgumentException"/> so existing catch blocks keep working.
/// On methods returning <c>ZibStack.NET.Result.Result</c>/<c>Result&lt;T&gt;</c> the generated
/// interceptor converts this into a failed Result with <c>Error.Validation</c> instead of throwing.
/// </summary>
public sealed class AspectValidationException : ArgumentException
{
    public AspectValidationException(string message, string? paramName) : base(message, paramName) { }
}
