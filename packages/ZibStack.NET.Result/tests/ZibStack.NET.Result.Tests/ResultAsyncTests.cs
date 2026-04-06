using Xunit;
using ZibStack.NET.Result;

namespace ZibStack.NET.Result.Tests;

public class ResultAsyncTests
{
    // ── MapAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task MapAsync_Success_TransformsValue()
    {
        var result = await Task.FromResult(Result<int>.Success(5))
            .MapAsync(x => x * 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public async Task MapAsync_Failure_PropagatesError()
    {
        var result = await Task.FromResult(Result<int>.Failure(Error.NotFound("x")))
            .MapAsync(x => x * 2);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error.Code);
    }

    [Fact]
    public async Task MapAsync_WithAsyncMapper_Success()
    {
        var result = await Task.FromResult(Result<int>.Success(5))
            .MapAsync(async x =>
            {
                await Task.Delay(1);
                return x.ToString();
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("5", result.Value);
    }

    // ── BindAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task BindAsync_Success_ChainsOperation()
    {
        var result = await Task.FromResult(Result<int>.Success(10))
            .BindAsync(x => Result<string>.Success($"val:{x}"));

        Assert.True(result.IsSuccess);
        Assert.Equal("val:10", result.Value);
    }

    [Fact]
    public async Task BindAsync_Failure_PropagatesError()
    {
        var result = await Task.FromResult(Result<int>.Failure(Error.Unexpected("boom")))
            .BindAsync(x => Result<string>.Success(x.ToString()));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task BindAsync_WithAsyncBinder_Success()
    {
        var result = await Task.FromResult(Result<int>.Success(5))
            .BindAsync(async x =>
            {
                await Task.Delay(1);
                return Result<string>.Success(x.ToString());
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("5", result.Value);
    }

    // ── Full async pipeline ───────────────────────────────────────────

    [Fact]
    public async Task Full_Async_Pipeline()
    {
        var result = await GetOrderAsync(1)
            .BindAsync(order => GetShipmentAsync(order))
            .MapAsync(shipment => $"shipped:{shipment}");

        Assert.True(result.IsSuccess);
        Assert.Equal("shipped:SHIP-1", result.Value);
    }

    [Fact]
    public async Task Full_Async_Pipeline_FailsAtFirstError()
    {
        var result = await GetOrderAsync(999) // will fail
            .BindAsync(order => GetShipmentAsync(order))
            .MapAsync(shipment => $"shipped:{shipment}");

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error.Code);
    }

    // ── TapAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task TapAsync_Success_ExecutesSideEffect()
    {
        var logged = "";
        var result = await Task.FromResult(Result<string>.Success("hello"))
            .TapAsync(v => logged = v);

        Assert.Equal("hello", logged);
        Assert.Equal("hello", result.Value);
    }

    // ── MatchAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_Success_CallsOnSuccess()
    {
        var output = await Task.FromResult(Result<int>.Success(42))
            .MatchAsync(
                v => $"ok:{v}",
                e => $"err:{e.Code}");

        Assert.Equal("ok:42", output);
    }

    // ── OrElseAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task OrElseAsync_Failure_ReturnsFallback()
    {
        var result = await Task.FromResult(Result<int>.Failure(Error.NotFound("x")))
            .OrElseAsync(_ => Result<int>.Success(0));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static Task<Result<string>> GetOrderAsync(int id)
        => Task.FromResult(id == 1
            ? Result<string>.Success("ORDER-1")
            : Result<string>.Failure(Error.NotFound($"Order {id} not found")));

    private static Task<Result<string>> GetShipmentAsync(string orderId)
        => Task.FromResult(Result<string>.Success($"SHIP-{orderId.Split('-')[1]}"));
}
