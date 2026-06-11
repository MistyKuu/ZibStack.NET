using ZibStack.NET.Aop.Tests.Fixtures;
using Xunit;

namespace ZibStack.NET.Aop.Tests;

// ── Aspect ↔ Result integration ─────────────────────────────────────────────
// Methods returning ZibStack.NET.Result.Result / Result<T> receive aspect
// precondition failures as failed Results instead of exceptions. The method's
// own exceptions (and aspect failures on non-Result methods) still throw.

[Collection("Aop")]
public class ResultIntegrationTests
{
    private readonly AopFixture _fx;

    public ResultIntegrationTests(AopFixture fx)
    {
        _fx = fx;
        _fx.AuthProvider.Reset();
    }

    [Fact]
    public async Task Authorize_Failure_OnResultT_ReturnsFailedResult()
    {
        var svc = new ResultAspectService();
        var result = await svc.AdminNumberAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("Unauthorized", result.Error!.Code);
        Assert.Contains("Admin", result.Error.Message);
    }

    [Fact]
    public async Task Authorize_Success_OnResultT_ReturnsValue()
    {
        _fx.AuthProvider.Roles.Add("Admin");
        try
        {
            var svc = new ResultAspectService();
            var result = await svc.AdminNumberAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(42, result.Value);
        }
        finally { _fx.AuthProvider.Reset(); }
    }

    [Fact]
    public async Task Authorize_Failure_OnNonGenericResult_ReturnsFailedResult()
    {
        var svc = new ResultAspectService();
        var result = await svc.AdminActionAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("Unauthorized", result.Error!.Code);
    }

    [Fact]
    public void Validate_Failure_OnResultT_ReturnsValidationError()
    {
        var svc = new ResultAspectService();
        var result = svc.Register(new ValidateRequest { Name = null, Age = 0 });

        Assert.True(result.IsFailure);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public void Validate_Success_OnResultT_ReturnsValue()
    {
        var svc = new ResultAspectService();
        var result = svc.Register(new ValidateRequest { Name = "Zib", Age = 30 });

        Assert.True(result.IsSuccess);
        Assert.Equal("ok:Zib", result.Value);
    }

    [Fact]
    public async Task MethodOwnException_OnResultT_StillThrows()
    {
        _fx.AuthProvider.Roles.Add("Admin");
        try
        {
            var svc = new ResultAspectService();
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AdminThrowsAsync());
        }
        finally { _fx.AuthProvider.Reset(); }
    }

    [Fact]
    public async Task Authorize_Failure_OnPlainReturnType_StillThrows()
    {
        var svc = new ResultAspectService();
        await Assert.ThrowsAsync<AspectAuthorizationException>(() => svc.AdminPlainAsync());
    }
}
