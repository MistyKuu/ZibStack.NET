using System;
using System.Collections.Generic;
using System.Linq;

namespace ZibStack.NET.Result;

/// <summary>
/// Utility extensions for working with collections of Results.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Combines multiple results into a single Result containing a list of values.
    /// If any result is a failure, returns the first error.
    /// </summary>
    public static Result<IReadOnlyList<T>> Combine<T>(this IEnumerable<Result<T>> results)
    {
        var values = new List<T>();
        foreach (var result in results)
        {
            if (result.IsFailure)
                return Result<IReadOnlyList<T>>.Failure(result.Error);
            values.Add(result.Value);
        }
        return Result<IReadOnlyList<T>>.Success(values);
    }

    /// <summary>
    /// Combines multiple results, collecting all errors into a single Validation error.
    /// </summary>
    public static Result<IReadOnlyList<T>> CombineAll<T>(this IEnumerable<Result<T>> results)
    {
        var values = new List<T>();
        var errors = new List<Error>();

        foreach (var result in results)
        {
            if (result.IsFailure)
                errors.Add(result.Error);
            else
                values.Add(result.Value);
        }

        if (errors.Count > 0)
            return Result<IReadOnlyList<T>>.Failure(
                Error.Validation("One or more operations failed.", errors));

        return Result<IReadOnlyList<T>>.Success(values);
    }

    /// <summary>
    /// Converts a nullable value to a Result, using the provided error if null.
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, Error error) where T : class
        => value is not null ? Result<T>.Success(value) : Result<T>.Failure(error);

    /// <summary>
    /// Converts a nullable value type to a Result, using the provided error if null.
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, Error error) where T : struct
        => value.HasValue ? Result<T>.Success(value.Value) : Result<T>.Failure(error);

    /// <summary>
    /// Ensures a condition is met on the value; otherwise returns the provided error.
    /// </summary>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error)
    {
        if (result.IsFailure)
            return result;

        return predicate(result.Value) ? result : Result<T>.Failure(error);
    }
}
