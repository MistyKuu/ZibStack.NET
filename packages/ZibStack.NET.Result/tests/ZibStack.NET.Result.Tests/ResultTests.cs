using Xunit;
using ZibStack.NET.Result;

namespace ZibStack.NET.Result.Tests;

public class ResultTests
{
    // ── Success / Failure ─────────────────────────────────────────────

    [Fact]
    public void Success_IsSuccess_ReturnsTrue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_IsFailure_ReturnsTrue()
    {
        var result = Result<int>.Failure(Error.NotFound("not found"));

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error.Code);
    }

    [Fact]
    public void Accessing_Value_On_Failure_Throws()
    {
        var result = Result<int>.Failure(Error.NotFound("nope"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Accessing_Error_On_Success_Throws()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    // ── Map ───────────────────────────────────────────────────────────

    [Fact]
    public void Map_Success_TransformsValue()
    {
        var result = Result<int>.Success(5)
            .Map(x => x * 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void Map_Failure_PropagatesError()
    {
        var error = Error.Unexpected("boom");
        var result = Result<int>.Failure(error)
            .Map(x => x * 2);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    // ── Bind ──────────────────────────────────────────────────────────

    [Fact]
    public void Bind_Success_ChainsOperation()
    {
        var result = Result<int>.Success(10)
            .Bind(x => x > 5
                ? Result<string>.Success($"big:{x}")
                : Result<string>.Failure(Error.Validation("too small")));

        Assert.True(result.IsSuccess);
        Assert.Equal("big:10", result.Value);
    }

    [Fact]
    public void Bind_Failure_PropagatesError()
    {
        var error = Error.NotFound("missing");
        var result = Result<int>.Failure(error)
            .Bind(x => Result<string>.Success(x.ToString()));

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Bind_Chain_StopsAtFirstError()
    {
        var result = Result<int>.Success(1)
            .Bind(x => Result<int>.Success(x + 1))
            .Bind(x => Result<int>.Failure(Error.Validation("stop here")))
            .Bind(x => Result<int>.Success(x + 100)); // should not execute

        Assert.True(result.IsFailure);
        Assert.Equal("stop here", result.Error.Message);
    }

    // ── Match ─────────────────────────────────────────────────────────

    [Fact]
    public void Match_Success_CallsOnSuccess()
    {
        var output = Result<int>.Success(42)
            .Match(
                v => $"value={v}",
                e => $"error={e.Code}");

        Assert.Equal("value=42", output);
    }

    [Fact]
    public void Match_Failure_CallsOnFailure()
    {
        var output = Result<int>.Failure(Error.Forbidden("nope"))
            .Match(
                v => $"value={v}",
                e => $"error={e.Code}");

        Assert.Equal("error=Forbidden", output);
    }

    // ── Tap ───────────────────────────────────────────────────────────

    [Fact]
    public void Tap_Success_ExecutesSideEffect()
    {
        var sideEffect = 0;
        var result = Result<int>.Success(7)
            .Tap(v => sideEffect = v);

        Assert.Equal(7, sideEffect);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void Tap_Failure_DoesNotExecute()
    {
        var executed = false;
        Result<int>.Failure(Error.Unexpected("err"))
            .Tap(_ => executed = true);

        Assert.False(executed);
    }

    // ── GetValueOrDefault ─────────────────────────────────────────────

    [Fact]
    public void GetValueOrDefault_Success_ReturnsValue()
    {
        var result = Result<int>.Success(42);

        Assert.Equal(42, result.GetValueOrDefault(0));
    }

    [Fact]
    public void GetValueOrDefault_Failure_ReturnsDefault()
    {
        var result = Result<int>.Failure(Error.NotFound("x"));

        Assert.Equal(0, result.GetValueOrDefault(0));
    }

    [Fact]
    public void GetValueOrDefault_Factory_Failure_InvokesFactory()
    {
        var result = Result<string>.Failure(Error.NotFound("x"));

        Assert.Equal("fallback:NotFound", result.GetValueOrDefault(e => $"fallback:{e.Code}"));
    }

    // ── OrElse ────────────────────────────────────────────────────────

    [Fact]
    public void OrElse_Success_ReturnsOriginal()
    {
        var result = Result<int>.Success(1)
            .OrElse(_ => Result<int>.Success(999));

        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void OrElse_Failure_ReturnsFallback()
    {
        var result = Result<int>.Failure(Error.NotFound("x"))
            .OrElse(_ => Result<int>.Success(999));

        Assert.Equal(999, result.Value);
    }

    // ── Implicit conversions ──────────────────────────────────────────

    [Fact]
    public void Implicit_Value_CreatesSuccess()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Implicit_Error_CreatesFailure()
    {
        Result<int> result = Error.NotFound("gone");

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error.Code);
    }

    // ── Non-generic Result ────────────────────────────────────────────

    [Fact]
    public void NonGeneric_Success()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NonGeneric_Failure()
    {
        var result = Result.Failure(Error.Unexpected("boom"));

        Assert.True(result.IsFailure);
        Assert.Equal("Unexpected", result.Error.Code);
    }

    [Fact]
    public void NonGeneric_Match()
    {
        var result = Result.Failure(Error.Conflict("dup"));

        var output = result.Match(
            () => "ok",
            e => $"err:{e.Code}");

        Assert.Equal("err:Conflict", output);
    }

    // ── ToResult ──────────────────────────────────────────────────────

    [Fact]
    public void ToResult_DiscardValue()
    {
        var typed = Result<int>.Success(42);
        var untyped = typed.ToResult();

        Assert.True(untyped.IsSuccess);
    }

    [Fact]
    public void ToResult_PreservesError()
    {
        var typed = Result<int>.Failure(Error.NotFound("x"));
        var untyped = typed.ToResult();

        Assert.True(untyped.IsFailure);
        Assert.Equal("NotFound", untyped.Error.Code);
    }
}
