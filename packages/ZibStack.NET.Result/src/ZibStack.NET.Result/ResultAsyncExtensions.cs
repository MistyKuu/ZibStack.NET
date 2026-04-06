using System;
using System.Threading.Tasks;

namespace ZibStack.NET.Result;

/// <summary>
/// Async extensions for Result&lt;T&gt;, enabling fluent async pipelines.
/// </summary>
public static class ResultAsyncExtensions
{
    // ── Map ───────────────────────────────────────────────────────────

    public static async Task<Result<TOut>> MapAsync<T, TOut>(
        this Task<Result<T>> resultTask, Func<T, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(map);
    }

    public static async Task<Result<TOut>> MapAsync<T, TOut>(
        this Task<Result<T>> resultTask, Func<T, Task<TOut>> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        if (result.IsFailure)
            return Result<TOut>.Failure(result.Error);

        var mapped = await map(result.Value).ConfigureAwait(false);
        return Result<TOut>.Success(mapped);
    }

    public static async ValueTask<Result<TOut>> MapAsync<T, TOut>(
        this ValueTask<Result<T>> resultTask, Func<T, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Map(map);
    }

    // ── Bind ──────────────────────────────────────────────────────────

    public static async Task<Result<TOut>> BindAsync<T, TOut>(
        this Task<Result<T>> resultTask, Func<T, Result<TOut>> bind)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Bind(bind);
    }

    public static async Task<Result<TOut>> BindAsync<T, TOut>(
        this Task<Result<T>> resultTask, Func<T, Task<Result<TOut>>> bind)
    {
        var result = await resultTask.ConfigureAwait(false);
        if (result.IsFailure)
            return Result<TOut>.Failure(result.Error);

        return await bind(result.Value).ConfigureAwait(false);
    }

    public static async ValueTask<Result<TOut>> BindAsync<T, TOut>(
        this ValueTask<Result<T>> resultTask, Func<T, Result<TOut>> bind)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Bind(bind);
    }

    public static async ValueTask<Result<TOut>> BindAsync<T, TOut>(
        this ValueTask<Result<T>> resultTask, Func<T, ValueTask<Result<TOut>>> bind)
    {
        var result = await resultTask.ConfigureAwait(false);
        if (result.IsFailure)
            return Result<TOut>.Failure(result.Error);

        return await bind(result.Value).ConfigureAwait(false);
    }

    // ── Match ─────────────────────────────────────────────────────────

    public static async Task<TOut> MatchAsync<T, TOut>(
        this Task<Result<T>> resultTask, Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    public static async Task<TOut> MatchAsync<T, TOut>(
        this Task<Result<T>> resultTask, Func<T, Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.IsSuccess
            ? await onSuccess(result.Value).ConfigureAwait(false)
            : await onFailure(result.Error).ConfigureAwait(false);
    }

    // ── Tap ───────────────────────────────────────────────────────────

    public static async Task<Result<T>> TapAsync<T>(
        this Task<Result<T>> resultTask, Action<T> action)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Tap(action);
    }

    public static async Task<Result<T>> TapAsync<T>(
        this Task<Result<T>> resultTask, Func<T, Task> action)
    {
        var result = await resultTask.ConfigureAwait(false);
        if (result.IsSuccess)
            await action(result.Value).ConfigureAwait(false);
        return result;
    }

    // ── Switch ────────────────────────────────────────────────────────

    public static async Task SwitchAsync<T>(
        this Task<Result<T>> resultTask, Action<T> onSuccess, Action<Error> onFailure)
    {
        var result = await resultTask.ConfigureAwait(false);
        result.Switch(onSuccess, onFailure);
    }

    // ── GetValueOrDefault ─────────────────────────────────────────────

    public static async Task<T> GetValueOrDefaultAsync<T>(
        this Task<Result<T>> resultTask, T defaultValue)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.GetValueOrDefault(defaultValue);
    }

    // ── OrElse ────────────────────────────────────────────────────────

    public static async Task<Result<T>> OrElseAsync<T>(
        this Task<Result<T>> resultTask, Func<Error, Result<T>> fallback)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.OrElse(fallback);
    }

    public static async Task<Result<T>> OrElseAsync<T>(
        this Task<Result<T>> resultTask, Func<Error, Task<Result<T>>> fallback)
    {
        var result = await resultTask.ConfigureAwait(false);
        if (result.IsSuccess)
            return result;

        return await fallback(result.Error).ConfigureAwait(false);
    }
}
