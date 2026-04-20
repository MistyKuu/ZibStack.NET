using ZibStack.NET.Log.Tests.Fixtures;
using Xunit;
using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log.Tests;

/// <summary>
/// Verifies that <c>b.Apply&lt;LogAttribute&gt;()</c> via <see cref="ZibStack.NET.Aop.IAopConfigurator"/>
/// actually generates logging interceptors for plain services that carry no <c>[Log]</c> attribute.
/// All logging is applied globally by <see cref="LogApplyTestConfig"/>.
/// </summary>
[Collection("Log")]
public class ApplyLogTests
{
    private readonly CapturingLoggerProvider _logProvider;

    public ApplyLogTests(LogFixture fixture)
    {
        _logProvider = fixture.LogProvider;
        _logProvider.Clear();
    }

    [Fact]
    public void ApplyLog_LogsEntryAndExit()
    {
        var svc = new E2ePaymentService();
        var result = svc.Charge("Alice", 99.99m);

        Assert.Equal("charged-Alice-99.99", result);
        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Entering") && e.Message.Contains("Charge"));
        Assert.Contains(entries, e => e.Message.Contains("Exited") && e.Message.Contains("Charge"));
    }

    [Fact]
    public void ApplyLog_MultipleServices_AllLogged()
    {
        var payments = new E2ePaymentService();
        var shipping = new E2eShippingService();

        payments.GetBalance(42);
        shipping.Ship("123 Main St");

        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("GetBalance"));
        Assert.Contains(entries, e => e.Message.Contains("Ship"));
    }

    [Fact]
    public async Task ApplyLog_AsyncMethod_Logged()
    {
        var svc = new E2eShippingService();
        var result = await svc.TrackAsync("TRK-001");

        Assert.True(result);
        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Entering") && e.Message.Contains("TrackAsync"));
        Assert.Contains(entries, e => e.Message.Contains("Exited") && e.Message.Contains("TrackAsync"));
    }

    [Fact]
    public void ApplyLog_Exception_LogsError()
    {
        var svc = new E2eThrowingService();
        Assert.Throws<InvalidOperationException>(() => svc.Explode());

        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Entering") && e.Message.Contains("Explode"));
        Assert.Contains(entries, e => e.Level == LogLevel.Error && e.Message.Contains("failed"));
    }

    [Fact]
    public void ApplyLog_ReturnValueLogged()
    {
        var svc = new E2ePaymentService();
        svc.GetBalance(7);

        var entries = _logProvider.AllEntries;
        var exitLog = entries.First(e => e.Message.Contains("Exited") && e.Message.Contains("GetBalance"));
        Assert.Contains("700", exitLog.Message);
    }

    [Fact]
    public void ApplyLog_ParametersLogged()
    {
        var svc = new E2ePaymentService();
        svc.Charge("Bob", 42.50m);

        var entries = _logProvider.AllEntries;
        var entryLog = entries.First(e => e.Message.Contains("Entering") && e.Message.Contains("Charge"));
        Assert.Contains("Bob", entryLog.Message);
    }

    [Fact]
    public void ApplyLog_ControlGroup_NotLogged()
    {
        _logProvider.Clear();
        var svc = new PlainUninstrumentedService();
        svc.DoWork(42);

        Assert.Empty(_logProvider.AllEntries);
    }

    [Fact]
    public void ApplyLog_ElapsedTimeLogged()
    {
        var svc = new E2ePaymentService();
        svc.Charge("Carol", 10m);

        var entries = _logProvider.AllEntries;
        var exitLog = entries.First(e => e.Message.Contains("Exited") && e.Message.Contains("Charge"));
        Assert.Matches(@"\d+ms", exitLog.Message);
    }
}
