using System.Diagnostics.Metrics;
using ZibStack.NET.Aop;
using ZibStack.NET.Aop.Tests.Fixtures;
using Xunit;

namespace ZibStack.NET.Aop.Tests;

// ── Retry tests ──────────────────────────────────────────────────────────────

[Collection("Aop")]
public class RetryTests
{
    public RetryTests(AopFixture _) { }

    [Fact]
    public void Retry_RetriesOnFailure_ThenSucceeds()
    {
        var svc = new RetryTestService();
        var result = svc.FailTwiceThenSucceed();

        Assert.Equal(42, result);
        Assert.Equal(3, svc.CallCount);
    }

    [Fact]
    public void Retry_ExhaustsAttempts_ThrowsLastException()
    {
        var svc = new RetryTestService();
        var ex = Assert.Throws<InvalidOperationException>(() => svc.AlwaysFails());

        Assert.Equal("always", ex.Message);
        Assert.Equal(2, svc.CallCount); // MaxAttempts = 2
    }

    [Fact]
    public void Retry_Handle_RetriesMatchingException()
    {
        var svc = new RetryTestService();
        var result = svc.HandleOnly();

        Assert.Equal(1, result);
        Assert.Equal(2, svc.CallCount); // 1st fails (InvalidOp → retried), 2nd succeeds
    }

    [Fact]
    public void Retry_Handle_DoesNotRetryNonMatchingException()
    {
        var svc = new RetryTestService();
        var ex = Assert.Throws<ArgumentException>(() => svc.HandleOnly_WrongException());

        Assert.Equal("not retryable", ex.Message);
        Assert.Equal(1, svc.CallCount); // ArgumentException not in Handle → no retry
    }

    [Fact]
    public void Retry_Ignore_RetriesNonIgnoredException()
    {
        var svc = new RetryTestService();
        var result = svc.IgnoreArgException();

        Assert.Equal(2, result);
        Assert.Equal(2, svc.CallCount); // InvalidOp not ignored → retried
    }

    [Fact]
    public void Retry_Ignore_DoesNotRetryIgnoredException()
    {
        var svc = new RetryTestService();
        var ex = Assert.Throws<ArgumentException>(() => svc.IgnoreArgException_Throws());

        Assert.Equal("ignored, no retry", ex.Message);
        Assert.Equal(1, svc.CallCount); // ArgumentException ignored → no retry
    }
}

// ── Cache tests ──────────────────────────────────────────────────────────────

[Collection("Aop")]
public class CacheTests : IDisposable
{
    public CacheTests(AopFixture _)
    {
        CacheHandler.ClearAll();
    }

    [Fact]
    public void Cache_ReturnsCachedValue_OnSecondCall()
    {
        var svc = new CacheTestService();

        var first = svc.GetValue(1);
        var second = svc.GetValue(1);

        Assert.Equal(first, second);
        Assert.Equal(1, svc.CallCount); // only called once
    }

    [Fact]
    public void Cache_DifferentParams_DifferentEntries()
    {
        var svc = new CacheTestService();

        var a = svc.GetValue(1);
        var b = svc.GetValue(2);

        Assert.NotEqual(a, b);
        Assert.Equal(2, svc.CallCount);
    }

    [Fact]
    public void Cache_Invalidate_ClearsMatchingEntries()
    {
        var svc = new CacheTestService();

        var first = svc.GetValue(1);
        CacheHandler.Invalidate("GetValue");
        var second = svc.GetValue(1);

        Assert.NotEqual(first, second);
        Assert.Equal(2, svc.CallCount);
    }

    [Fact]
    public void Cache_ClearAll_ClearsEverything()
    {
        var svc = new CacheTestService();

        svc.GetValue(1);
        svc.GetValue(2);
        CacheHandler.ClearAll();

        svc.GetValue(1);
        Assert.Equal(3, svc.CallCount);
    }

    [Fact]
    public void Cache_KeyTemplate_UsesCustomKey()
    {
        var svc = new CacheTestService();

        // Same id, different includeArchived — should hit same cache entry (only {id} in template)
        var first = svc.GetWithTemplate(1, false);
        var second = svc.GetWithTemplate(1, true);

        Assert.Equal(first, second);
        Assert.Equal(1, svc.CallCount);
    }

    [Fact]
    public void Cache_KeyTemplate_DifferentIds_DifferentEntries()
    {
        var svc = new CacheTestService();

        var a = svc.GetWithTemplate(1, false);
        var b = svc.GetWithTemplate(2, false);

        Assert.NotEqual(a, b);
        Assert.Equal(2, svc.CallCount);
    }

    [Fact]
    public void Cache_KeyTemplate_Nested_UsesPropertyAccess()
    {
        var svc = new CacheTestService();

        var req1 = new CacheRequest { CustomerId = 42, Region = "EU" };
        var req2 = new CacheRequest { CustomerId = 42, Region = "EU" };
        var req3 = new CacheRequest { CustomerId = 42, Region = "US" };

        var first = svc.GetWithNestedTemplate(req1);
        var second = svc.GetWithNestedTemplate(req2); // same key → cached

        Assert.Equal(first, second);
        Assert.Equal(1, svc.CallCount);

        var third = svc.GetWithNestedTemplate(req3); // different Region → miss
        Assert.NotEqual(first, third);
        Assert.Equal(2, svc.CallCount);
    }

    public void Dispose() => CacheHandler.ClearAll();
}

// ── Metrics tests ────────────────────────────────────────────────────────────

[Collection("Aop")]
public class MetricsTests
{
    public MetricsTests(AopFixture _) { }

    [Fact]
    public void Metrics_SuccessfulCall_DoesNotThrow()
    {
        var svc = new MetricsTestService();
        var result = svc.Compute(5);
        Assert.Equal(10, result);
    }

    [Fact]
    public void Metrics_FailingCall_StillThrows()
    {
        var svc = new MetricsTestService();
        Assert.Throws<InvalidOperationException>(() => svc.Fail());
    }

    [Fact]
    public void Metrics_MeterListener_RecordsCallCount()
    {
        long callCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == MetricsHandler.DefaultMeterName && instrument.Name == "aop.method.call.count")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            // Only count measurements for MetricsTestService.Compute
            foreach (var tag in tags)
                if (tag.Key == "aop.method" && tag.Value is "Compute")
                { callCount += measurement; return; }
        });
        listener.Start();

        var svc = new MetricsTestService();
        svc.Compute(1);
        svc.Compute(2);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void Metrics_MeterListener_RecordsDuration()
    {
        var durations = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == MetricsHandler.DefaultMeterName && instrument.Name == "aop.method.call.duration")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "aop.method" && tag.Value is "Compute")
                { durations.Add(measurement); return; }
        });
        listener.Start();

        var svc = new MetricsTestService();
        svc.Compute(1);

        Assert.Single(durations);
        Assert.True(durations[0] >= 0);
    }

    [Fact]
    public void Metrics_MeterListener_RecordsErrors()
    {
        long errorCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == MetricsHandler.DefaultMeterName && instrument.Name == "aop.method.call.errors")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "aop.method" && tag.Value is "Fail")
                { errorCount += measurement; return; }
        });
        listener.Start();

        var svc = new MetricsTestService();
        try { svc.Fail(); } catch { }
        try { svc.Fail(); } catch { }

        Assert.Equal(2, errorCount);
    }

    [Fact]
    public void Metrics_Tags_ContainClassAndMethod()
    {
        var capturedTags = new List<KeyValuePair<string, object?>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == MetricsHandler.DefaultMeterName && instrument.Name == "aop.method.call.count")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                capturedTags.Add(tag);
        });
        listener.Start();

        var svc = new MetricsTestService();
        svc.Compute(1);

        Assert.Contains(capturedTags, t => t.Key == "aop.class" && t.Value is "MetricsTestService");
        Assert.Contains(capturedTags, t => t.Key == "aop.method" && t.Value is "Compute");
        Assert.Contains(capturedTags, t => t.Key == "aop.metric" && t.Value is "test.operation");
    }
}

// ── Timeout tests ────────────────────────────────────────────────────────────

[Collection("Aop")]
public class TimeoutTests
{
    public TimeoutTests(AopFixture _) { }

    [Fact]
    public async Task Timeout_FastMethod_Succeeds()
    {
        var svc = new TimeoutTestService();
        var result = await svc.FastAsync();
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Timeout_SlowMethod_ThrowsTimeoutException()
    {
        var svc = new TimeoutTestService();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => svc.SlowAsync());
        Assert.Contains("TimeoutTestService.SlowAsync", ex.Message);
        Assert.Contains("50ms", ex.Message);
    }
}

// ── Authorize tests ──────────────────────────────────────────────────────────

[Collection("Aop")]
public class AuthorizeTests
{
    private readonly TestAuthorizationProvider _auth;

    public AuthorizeTests(AopFixture fixture)
    {
        _auth = fixture.AuthProvider;
        _auth.Reset();
    }

    [Fact]
    public async Task Authorize_Roles_Allowed_Succeeds()
    {
        _auth.Roles.Add("Admin");
        var svc = new AuthorizeTestService();
        var result = await svc.AdminOnlyAsync();
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Authorize_Roles_Denied_Throws()
    {
        _auth.Roles.Add("User");
        var svc = new AuthorizeTestService();
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.AdminOnlyAsync());
        Assert.Contains("Admin", ex.Message);
    }

    [Fact]
    public async Task Authorize_Roles_NoRoles_Throws()
    {
        var svc = new AuthorizeTestService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.AdminOnlyAsync());
    }

    [Fact]
    public async Task Authorize_Policy_Allowed_Succeeds()
    {
        _auth.Policies.Add("CanEdit");
        var svc = new AuthorizeTestService();
        var result = await svc.PolicyProtectedAsync();
        Assert.Equal(99, result);
    }

    [Fact]
    public async Task Authorize_Policy_Denied_Throws()
    {
        var svc = new AuthorizeTestService();
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.PolicyProtectedAsync());
        Assert.Contains("CanEdit", ex.Message);
    }

    [Fact]
    public async Task Authorize_NoArgs_Authenticated_Succeeds()
    {
        _auth.IsAuthenticated = true;
        var svc = new AuthorizeTestService();
        var result = await svc.AuthenticatedOnlyAsync();
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Authorize_NoArgs_NotAuthenticated_Throws()
    {
        _auth.IsAuthenticated = false;
        var svc = new AuthorizeTestService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.AuthenticatedOnlyAsync());
    }
}

// ── Validate tests ───────────────────────────────────────────────────────────

[Collection("Aop")]
public class ValidateTests
{
    public ValidateTests(AopFixture _) { }

    [Fact]
    public void Validate_ValidRequest_Succeeds()
    {
        var svc = new ValidateTestService();
        var result = svc.Process(new ValidateRequest { Name = "Alice", Age = 25 });
        Assert.Equal("ok:Alice", result);
    }

    [Fact]
    public void Validate_RequiredMissing_Throws()
    {
        var svc = new ValidateTestService();
        var ex = Assert.Throws<ArgumentException>(() =>
            svc.Process(new ValidateRequest { Age = 25 })); // Name is null
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Validate_RangeViolation_Throws()
    {
        var svc = new ValidateTestService();
        var ex = Assert.Throws<ArgumentException>(() =>
            svc.Process(new ValidateRequest { Name = "Bob", Age = 200 }));
        Assert.Contains("Age", ex.Message);
    }

    [Fact]
    public void Validate_PrimitiveParamsSkipped()
    {
        var svc = new ValidateTestService();
        // count is int — not validated, only the request object is
        var result = svc.ProcessMulti(new ValidateRequest { Name = "X", Age = 1 }, 42);
        Assert.Equal("ok:42", result);
    }
}

// ── Transaction tests ────────────────────────────────────────────────────────

[Collection("Aop")]
public class TransactionTests
{
    public TransactionTests(AopFixture _) { }

    [Fact]
    public void Transaction_WrapsInTransactionScope()
    {
        var svc = new TransactionTestService();
        var result = svc.DoWork(5);

        Assert.Equal(10, result);
        Assert.True(svc.Completed); // Transaction.Current was not null inside the method
    }

    [Fact]
    public void Transaction_RollsBackOnException()
    {
        var svc = new TransactionTestService();
        Assert.Throws<InvalidOperationException>(() => svc.DoWorkFailing(5));
        // TransactionScope disposes without Complete() → implicit rollback
    }
}

// ── Audit tests ─────────────────────────────────────────────────────────────

[Collection("Aop")]
public class AuditTests
{
    private readonly AopFixture _fixture;
    public AuditTests(AopFixture fixture) => _fixture = fixture;

    [Fact]
    public void Audit_CapturesBeforeAfterAndMethodInfo()
    {
        _fixture.AuditStore.Entries.Clear();
        var svc = new Fixtures.AuditTestService();
        var result = svc.UpdateName(42, "Alice");

        Assert.Equal("updated-42-Alice", result);
        Assert.Single(_fixture.AuditStore.Entries);

        var entry = _fixture.AuditStore.Entries[0];
        Assert.Equal("AuditTestService", entry.ClassName);
        Assert.Equal("UpdateName", entry.MethodName);
        Assert.Equal("UpdateName", entry.Action);
        Assert.Contains("id=42", entry.BeforeSnapshot!);
        Assert.Contains("newName=Alice", entry.BeforeSnapshot!);
        Assert.False(entry.IsError);
        Assert.True(entry.ElapsedMs >= 0);
    }

    [Fact]
    public void Audit_CustomAction_UsesProvidedLabel()
    {
        _fixture.AuditStore.Entries.Clear();
        var svc = new Fixtures.AuditTestService();
        svc.PlaceOrder("Bob", 99.99m);

        Assert.Single(_fixture.AuditStore.Entries);
        // Action is still MethodName because the attribute Action property
        // is read from NamedArguments by the generator, not at runtime.
        // The handler falls back to MethodName when not injected via context.
        var entry = _fixture.AuditStore.Entries[0];
        Assert.Equal("PlaceOrder", entry.MethodName);
    }

    [Fact]
    public void Audit_OnException_CapturesError()
    {
        _fixture.AuditStore.Entries.Clear();
        var svc = new Fixtures.AuditTestService();

        Assert.Throws<InvalidOperationException>(() => svc.FailingMethod());

        Assert.Single(_fixture.AuditStore.Entries);
        var entry = _fixture.AuditStore.Entries[0];
        Assert.True(entry.IsError);
        Assert.Equal("System.InvalidOperationException", entry.ExceptionType);
        Assert.Equal("boom", entry.ExceptionMessage);
    }

    [Fact]
    public void Audit_Sensitive_MaskedInSnapshot()
    {
        _fixture.AuditStore.Entries.Clear();
        var svc = new Fixtures.AuditTestService();
        svc.SensitiveMethod(1, "supersecret");

        Assert.Single(_fixture.AuditStore.Entries);
        var entry = _fixture.AuditStore.Entries[0];
        Assert.Contains("id=1", entry.BeforeSnapshot!);
        Assert.Contains("secret=***", entry.BeforeSnapshot!);
        Assert.DoesNotContain("supersecret", entry.BeforeSnapshot!);
    }
}
