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

    // ── Interface dispatch (the real DI shape) ─────────────────────────────
    // Apply rule uses ClassesWhere(c => c.Name.StartsWith("E2e")) which matches
    // E2ePaymentService but NOT IE2ePaymentService. The generator must synthesize
    // an interface proxy so calls through the interface are still intercepted.

    [Fact]
    public void ApplyLog_InterfaceDispatch_StillLogged()
    {
        IE2ePaymentService svc = new E2ePaymentService();
        svc.Charge("Dave", 77m);

        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Entering") && e.Message.Contains("Charge"));
        Assert.Contains(entries, e => e.Message.Contains("Exited") && e.Message.Contains("Charge"));
    }

    [Fact]
    public void ApplyLog_InterfaceDispatch_ReturnValueLogged()
    {
        IE2ePaymentService svc = new E2ePaymentService();
        svc.GetBalance(3);

        var entries = _logProvider.AllEntries;
        var exitLog = entries.First(e => e.Message.Contains("Exited") && e.Message.Contains("GetBalance"));
        Assert.Contains("300", exitLog.Message);
    }

    // ── Apply(selector, configure) — aspect configuration at the rule level ──
    // Verifies that the second lambda on b.Apply<LogAttribute>() actually feeds
    // LogParameters=false into the emitted interceptor. Historically the flag
    // was ignored because LogClassDataProvider only wired up global defaults
    // when the class carried a method-level [Log] attribute.

    [Fact]
    public void ApplyLog_ConfigureLambda_LogParametersFalse_OmitsArgs()
    {
        var svc = new NoParamsService();
        svc.Charge("Alice", 99.99m);

        var entries = _logProvider.AllEntries;
        var entryLog = entries.First(e => e.Message.Contains("Entering") && e.Message.Contains("Charge"));
        Assert.DoesNotContain("Alice", entryLog.Message);
        Assert.DoesNotContain("99.99", entryLog.Message);
    }

    // ── [NoLog] parameter via interface dispatch ─────────────────────────────
    // Covers both placements of the attribute: on the interface parameter and
    // on the impl parameter. With the bug fix, both paths redact the value
    // from the proxy's entry log.

    [Fact]
    public void NoLogOnInterfaceParam_InterfaceDispatch_Redacted()
    {
        INoLogIfaceParamService svc = new NoLogIfaceParamServiceImpl();
        _ = svc.DoThing("ok", "topsecret");

        var entry = _logProvider.AllEntries.First(e =>
            e.Message.Contains("Entering") && e.Message.Contains("DoThing"));
        Assert.Contains("ok", entry.Message);
        Assert.DoesNotContain("topsecret", entry.Message);
    }

    [Fact]
    public void NoLogOnImplParam_InterfaceDispatch_Redacted()
    {
        INoLogImplParamService svc = new NoLogImplParamServiceImpl();
        _ = svc.DoThing("ok", "topsecret");

        var entry = _logProvider.AllEntries.First(e =>
            e.Message.Contains("Entering") && e.Message.Contains("DoThing"));
        Assert.Contains("ok", entry.Message);
        Assert.DoesNotContain("topsecret", entry.Message);
    }
}
