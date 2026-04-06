using ZibStack.NET.Result;

namespace ZibStack.NET.Result.Tests;

public class ResultExtensionsTests
{
    // ── Combine ───────────────────────────────────────────────────────

    [Fact]
    public void Combine_AllSuccess_ReturnsList()
    {
        var results = new[]
        {
            Result<int>.Success(1),
            Result<int>.Success(2),
            Result<int>.Success(3),
        };

        var combined = results.Combine();

        Assert.True(combined.IsSuccess);
        Assert.Equal(new[] { 1, 2, 3 }, combined.Value);
    }

    [Fact]
    public void Combine_OneFailure_ReturnsFirstError()
    {
        var results = new[]
        {
            Result<int>.Success(1),
            Result<int>.Failure(Error.Validation("bad")),
            Result<int>.Success(3),
        };

        var combined = results.Combine();

        Assert.True(combined.IsFailure);
        Assert.Equal("bad", combined.Error.Message);
    }

    // ── CombineAll ────────────────────────────────────────────────────

    [Fact]
    public void CombineAll_CollectsAllErrors()
    {
        var results = new[]
        {
            Result<int>.Failure(Error.Validation("err1")),
            Result<int>.Success(2),
            Result<int>.Failure(Error.Validation("err2")),
        };

        var combined = results.CombineAll();

        Assert.True(combined.IsFailure);
        Assert.Equal(2, combined.Error.InnerErrors.Count);
        Assert.Equal("err1", combined.Error.InnerErrors[0].Message);
        Assert.Equal("err2", combined.Error.InnerErrors[1].Message);
    }

    // ── ToResult (reference type) ─────────────────────────────────────

    [Fact]
    public void ToResult_NotNull_ReturnsSuccess()
    {
        string? value = "hello";
        var result = value.ToResult(Error.NotFound("missing"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void ToResult_Null_ReturnsFailure()
    {
        string? value = null;
        var result = value.ToResult(Error.NotFound("missing"));

        Assert.True(result.IsFailure);
    }

    // ── ToResult (value type) ─────────────────────────────────────────

    [Fact]
    public void ToResult_NullableStruct_HasValue_ReturnsSuccess()
    {
        int? value = 42;
        var result = value.ToResult(Error.NotFound("missing"));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ToResult_NullableStruct_Null_ReturnsFailure()
    {
        int? value = null;
        var result = value.ToResult(Error.NotFound("missing"));

        Assert.True(result.IsFailure);
    }

    // ── Ensure ────────────────────────────────────────────────────────

    [Fact]
    public void Ensure_PredicateTrue_ReturnsOriginal()
    {
        var result = Result<int>.Success(10)
            .Ensure(x => x > 5, Error.Validation("too small"));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public void Ensure_PredicateFalse_ReturnsFailure()
    {
        var result = Result<int>.Success(3)
            .Ensure(x => x > 5, Error.Validation("too small"));

        Assert.True(result.IsFailure);
        Assert.Equal("too small", result.Error.Message);
    }

    [Fact]
    public void Ensure_OnFailure_PropagatesOriginalError()
    {
        var result = Result<int>.Failure(Error.NotFound("gone"))
            .Ensure(x => x > 5, Error.Validation("too small"));

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error.Code);
    }
}
