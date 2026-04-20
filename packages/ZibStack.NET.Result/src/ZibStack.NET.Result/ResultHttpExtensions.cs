#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.Http;

namespace ZibStack.NET.Result;

/// <summary>
/// Extension methods for converting <see cref="Result{T}"/> to ASP.NET Core <see cref="IResult"/>.
/// Maps <see cref="Error.Code"/> to standard HTTP status codes.
/// </summary>
public static class ResultHttpExtensions
{
    /// <summary>
    /// Converts a <see cref="Result{T}"/> to an <see cref="IResult"/>:
    /// <list type="bullet">
    /// <item>Success → 200 OK with the value</item>
    /// <item>Failure → HTTP status based on <see cref="Error.Code"/></item>
    /// </list>
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
        => result.Match(
            onSuccess: value => Results.Ok(value),
            onFailure: ErrorToHttpResult);

    /// <summary>
    /// Converts a <see cref="Result{T}"/> to an <see cref="IResult"/> with 201 Created on success.
    /// </summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, string? uri = null)
        => result.Match(
            onSuccess: value => Results.Created(uri, value),
            onFailure: ErrorToHttpResult);

    /// <summary>
    /// Converts a non-generic <see cref="Result"/> to an <see cref="IResult"/>:
    /// Success → 204 NoContent, Failure → mapped HTTP error.
    /// </summary>
    public static IResult ToHttpResult(this Result result)
        => result.Match(
            onSuccess: () => Results.NoContent(),
            onFailure: ErrorToHttpResult);

    private static IResult ErrorToHttpResult(Error error) => error.Code switch
    {
        "NotFound" => Results.NotFound(new { error = error.Message }),
        "Validation" => Results.BadRequest(new { error = error.Message, details = FormatInnerErrors(error) }),
        "Conflict" => Results.Conflict(new { error = error.Message }),
        "Unauthorized" => Results.Unauthorized(),
        "Forbidden" => Results.Forbid(),
        _ => Results.Problem(error.Message, statusCode: 500),
    };

    private static object? FormatInnerErrors(Error error)
        => error.InnerErrors.Count > 0
            ? error.InnerErrors.Select(e => new { e.Code, e.Message }).ToArray()
            : null;
}
#endif
