using System;

namespace ZibStack.NET.Result;

/// <summary>
/// Represents the outcome of an operation that does not return a value.
/// </summary>
public readonly struct Result
{
    private readonly Error? _error;

    private Result(Error? error)
    {
        _error = error;
    }

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;

    public Error Error => _error ?? throw new InvalidOperationException("Cannot access Error on a successful result.");

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    /// <summary>
    /// Pattern match on the result.
    /// </summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
        => IsSuccess ? onSuccess() : onFailure(_error!);

    /// <summary>
    /// Execute an action based on the result.
    /// </summary>
    public void Switch(Action onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess)
            onSuccess();
        else
            onFailure(_error!);
    }

    public override string ToString()
        => IsSuccess ? "Success" : $"Failure({_error})";
}

/// <summary>
/// Represents the outcome of an operation that returns a value of type <typeparamref name="T"/>.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
    }

    private Result(Error error)
    {
        _value = default;
        _error = error;
    }

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public Error Error => _error ?? throw new InvalidOperationException("Cannot access Error on a successful result.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>
    /// Transform the success value. If the result is a failure, the error propagates automatically.
    /// </summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        => IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(_error!);

    /// <summary>
    /// Chain another Result-returning operation. If the result is a failure, the error propagates automatically.
    /// </summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
        => IsSuccess ? bind(_value!) : Result<TOut>.Failure(_error!);

    /// <summary>
    /// Pattern match on the result.
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    /// <summary>
    /// Execute an action based on the result.
    /// </summary>
    public void Switch(Action<T> onSuccess, Action<Error> onFailure)
    {
        if (IsSuccess)
            onSuccess(_value!);
        else
            onFailure(_error!);
    }

    /// <summary>
    /// Execute a side effect on success, then return the original result.
    /// </summary>
    public Result<T> Tap(Action<T> action)
    {
        if (IsSuccess)
            action(_value!);
        return this;
    }

    /// <summary>
    /// Returns the value if success, or the provided default if failure.
    /// </summary>
    public T GetValueOrDefault(T defaultValue)
        => IsSuccess ? _value! : defaultValue;

    /// <summary>
    /// Returns the value if success, or invokes the factory if failure.
    /// </summary>
    public T GetValueOrDefault(Func<Error, T> factory)
        => IsSuccess ? _value! : factory(_error!);

    /// <summary>
    /// Convert to a non-generic Result, discarding the value.
    /// </summary>
    public Result ToResult()
        => IsSuccess ? Result.Success() : Result.Failure(_error!);

    /// <summary>
    /// Provide a fallback Result if this one failed.
    /// </summary>
    public Result<T> OrElse(Func<Error, Result<T>> fallback)
        => IsSuccess ? this : fallback(_error!);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);

    public override string ToString()
        => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
}
