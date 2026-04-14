using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZibStack.NET.Aop;
using ZibStack.NET.Log.Tests.Fixtures;
using Xunit;

namespace ZibStack.NET.Log.Tests;

// ── Serialize all tests that touch the static AspectServiceProvider ─────────

[CollectionDefinition("Log")]
public class LogCollection : ICollectionFixture<LogFixture> { }

public class LogFixture : IDisposable
{
    public CapturingLoggerProvider LogProvider { get; } = new();

    public LogFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddProvider(LogProvider);
            b.SetMinimumLevel(LogLevel.Trace);
        });
        var sp = services.BuildServiceProvider();
        AspectServiceProvider.ServiceProvider = sp;
    }

    public void Dispose() { }
}

// ── Behavioral tests — verify [Log] actually logs at runtime ────────────────

[Collection("Log")]
public class ZibLogBehaviorTests
{
    private readonly CapturingLoggerProvider _logProvider;

    public ZibLogBehaviorTests(LogFixture fixture)
    {
        _logProvider = fixture.LogProvider;
        _logProvider.Clear();
    }

    // ── Method-level [Log] ──

    [Fact]
    public void MethodLevelLog_LogsEntryAndExit()
    {
        var svc = new SimpleLogService();
        var result = svc.Add(2, 3);

        Assert.Equal(5, result);
        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Entering") && e.Message.Contains("Add"));
        Assert.Contains(entries, e => e.Message.Contains("Exited") && e.Message.Contains("Add"));
    }

    [Fact]
    public void MethodWithoutLog_DoesNotLog()
    {
        _logProvider.Clear();
        var svc = new SimpleLogService();
        _ = svc.Plain(42);

        Assert.Empty(_logProvider.AllEntries);
    }

    // ── Class-level [Log] ──

    [Fact]
    public void ClassLevelLog_LogsAllMethods()
    {
        var svc = new ClassLogService();
        _ = svc.Multiply(3, 4);

        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Multiply"));

        _logProvider.Clear();
        _ = svc.Echo("hi");

        entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Echo"));
    }

    // ── Async ──

    [Fact]
    public async Task AsyncMethod_LogsEntryAndExit()
    {
        var svc = new AsyncLogService();
        var result = await svc.ComputeAsync(5);

        Assert.Equal(10, result);
        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Entering") && e.Message.Contains("ComputeAsync"));
        Assert.Contains(entries, e => e.Message.Contains("Exited") && e.Message.Contains("ComputeAsync"));
    }

    // ── Exception ──

    [Fact]
    public void Exception_LogsError()
    {
        var svc = new ThrowingLogService();
        Assert.Throws<InvalidOperationException>(() => svc.Fail());

        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Entering") && e.Message.Contains("Fail"));
        Assert.Contains(entries, e => e.Level == LogLevel.Error && e.Message.Contains("failed"));
    }

    // ── [Sensitive] parameter ──

    [Fact]
    public void SensitiveParam_MaskedInLog()
    {
        var svc = new SensitiveLogService();
        _ = svc.Login("alice", "s3cret");

        var entries = _logProvider.AllEntries;
        var entryLog = entries.First(e => e.Message.Contains("Entering") && e.Message.Contains("Login"));
        Assert.Contains("alice", entryLog.Message);
        Assert.Contains("***", entryLog.Message);
        Assert.DoesNotContain("s3cret", entryLog.Message);
    }

    // ── Interface proxy ──

    [Fact]
    public void InterfaceProxy_LogsCallThroughInterface()
    {
        ILoggedOrderService svc = new LoggedOrderServiceImpl();
        var result = svc.GetOrder(42);

        Assert.Equal(42, result);
        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("GetOrder"));
    }

    // ── Return value logged ──

    [Fact]
    public void ExitLog_ContainsReturnValue()
    {
        var svc = new SimpleLogService();
        _ = svc.Add(10, 20);

        var entries = _logProvider.AllEntries;
        var exitLog = entries.First(e => e.Message.Contains("Exited") && e.Message.Contains("Add"));
        Assert.Contains("30", exitLog.Message);
    }

    // ── Elapsed time logged ──

    [Fact]
    public void ExitLog_ContainsElapsedMs()
    {
        var svc = new SimpleLogService();
        _ = svc.Add(1, 1);

        var entries = _logProvider.AllEntries;
        var exitLog = entries.First(e => e.Message.Contains("Exited") && e.Message.Contains("Add"));
        // The exit message contains elapsed ms (e.g., "in 0ms")
        Assert.Matches(@"\d+ms", exitLog.Message);
    }

    // ── Method-level [Log] on impl, call through interface ──

    [Fact]
    public void InterfaceProxy_MethodLevelLog_InterceptsCall()
    {
        ISelectiveLogService svc = new SelectiveLogServiceImpl();
        var result = svc.Tracked(5);

        Assert.Equal(5, result);
        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Tracked"));
    }

    [Fact]
    public void InterfaceProxy_MethodWithoutLog_NotLogged()
    {
        _logProvider.Clear();
        ISelectiveLogService svc = new SelectiveLogServiceImpl();
        _ = svc.Untracked(5);

        Assert.Empty(_logProvider.AllEntries);
    }

    // ── Generic class ──

    [Fact]
    public void GenericClass_ClassLevelLog_Logs()
    {
        var svc = new GenericLogRepo<string>();
        _ = svc.Get(1);

        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Get"));
    }

    // ── Generic method ──

    [Fact]
    public void GenericMethod_MethodLevelLog_Logs()
    {
        var svc = new GenericLogMethodService();
        _ = svc.Fetch<string>(1);

        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Fetch"));
    }

    // ── Inheritance ──

    [Fact]
    public void Inheritance_DerivedClass_InheritsLog()
    {
        var svc = new DerivedLogService();
        var result = svc.Process(5);

        Assert.Equal(10, result);
        var entries = _logProvider.AllEntries;
        Assert.Contains(entries, e => e.Message.Contains("Process"));
    }
}
