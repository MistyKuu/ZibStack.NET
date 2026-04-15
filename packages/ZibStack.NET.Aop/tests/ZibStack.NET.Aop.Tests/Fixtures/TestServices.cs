using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Tests.Fixtures;

// ── Method-level aspect ─────────────────────────────────────────────────────

public class MethodAspectService
{
    [Record]
    public int Add(int a, int b) => a + b;

    public int Plain(int x) => x; // no aspect — should NOT be intercepted
}

// ── Class-level aspect ──────────────────────────────────────────────────────

[Record]
public class ClassAspectService
{
    public int Multiply(int a, int b) => a * b;
    public string Echo(string msg) => $"Hello {msg}";
}

// ── Async ────────────────────────────────────────────────────────────────────

public class AsyncAspectService
{
    [Record]
    public async Task<int> ComputeAsync(int x)
    {
        await Task.Delay(1);
        return x * 2;
    }
}

// ── Exception ────────────────────────────────────────────────────────────────

public class ThrowingAspectService
{
    [Record]
    public int Fail() => throw new InvalidOperationException("boom");
}

// ── Around handler ──────────────────────────────────────────────────────────

public class AroundAspectService
{
    [RecordAround]
    public int Double(int x) => x * 2;
}

// ── Interface + class-level aspect ──────────────────────────────────────────

public interface IOrderAspectService
{
    int GetOrder(int id);
}

[Record]
public class OrderAspectServiceImpl : IOrderAspectService
{
    public int GetOrder(int id) => id;
}

// ── Interface + method-level aspect (selective) ─────────────────────────────

public interface ISelectiveService
{
    int Tracked(int x);
    int Untracked(int x);
}

public class SelectiveServiceImpl : ISelectiveService
{
    [Record]
    public int Tracked(int x) => x;

    public int Untracked(int x) => x; // no aspect
}

// ── Generic class ───────────────────────────────────────────────────────────

[Record]
public class GenericRepo<T> where T : class
{
    public T? Get(int id) => default;
}

// ── Generic method ──────────────────────────────────────────────────────────

public class GenericMethodService
{
    [Record]
    public T? Fetch<T>(int id) where T : class => default;
}

// ── Inheritance ─────────────────────────────────────────────────────────────

[Record]
public class BaseAspectService
{
    public virtual int Process(int x) => x * 2;
}

public class DerivedAspectService : BaseAspectService { }

// ── Multiple aspects on one method ──────────────────────────────────────────

public class MultiAspectService
{
    [Record]
    [RecordAround]
    public int Work(int x) => x + 1;
}

// ── Class-level aspect on class with internal methods ───────────────────────

[Record]
public class MixedAccessService
{
    public int PublicWork(int x) => x;
    internal int InternalWork(int x) => x;
}

// ── Static method with aspect ───────────────────────────────────────────────
//
// Behavioral test fixture for the AOP0001 analyzer claim ("aspect on static
// method has no effect"). If the handler IS called for these methods, the
// analyzer is a false positive and must be removed. If it ISN'T called, the
// analyzer is correct and the runtime silently ignores the aspect.
//
// AOP0001 is suppressed here because we intentionally apply [Record] to a static
// method to verify the runtime behavior the analyzer claims about — disabling
// the analyzer for these specific fixtures lets the test compile while still
// keeping AOP0001 active everywhere else in the project.

#pragma warning disable AOP0001
public static class StaticAspectService
{
    public static int CallCount;

    [Record]
    public static int GetValue(int id)
    {
        CallCount++;
        return id;
    }
}

[Record]
public class ClassLevelMixedService
{
    public static int StaticCallCount;
    public static int InstanceCallCount;

    public static int GetStatic(int id) { StaticCallCount++; return id; }
    public int GetInstance(int id) { InstanceCallCount++; return id; }
}
#pragma warning restore AOP0001

// ── AOP0002 ground truth: private method with method-level aspect ────────────
//
// Suppress AOP0002 because we INTENTIONALLY put [Record] on a private method to
// verify the analyzer's runtime claim. The wrapper exposes a public method that
// internally calls the private one, so the test can actually invoke it.

#pragma warning disable AOP0002
public class PrivateAspectService
{
    public int CallCount;

    [Record]
    private int Hidden(int id)
    {
        CallCount++;
        return id;
    }

    public int CallHidden(int id) => Hidden(id);
}
#pragma warning restore AOP0002

// ── AOP0006 ground truth: operator with aspect ───────────────────────────────

#pragma warning disable AOP0006
public class BoxAspect
{
    public static int OperatorCallCount;
    public int Value;

    [Record]
    public static BoxAspect operator +(BoxAspect a, BoxAspect b)
    {
        OperatorCallCount++;
        return new BoxAspect { Value = a.Value + b.Value };
    }
}
#pragma warning restore AOP0006

// ── AOP0010 ground truth: [Cache] on void method ─────────────────────────────

#pragma warning disable AOP0010
public class CacheVoidService
{
    public int CallCount;

    [Cache(DurationSeconds = 60)]
    public void DoWork()
    {
        CallCount++;
    }
}
#pragma warning restore AOP0010

// ── Polly add-on package fixtures ───────────────────────────────────────────

public class PollyRetryService
{
    public int CallCount;

    [PollyRetry(MaxRetryAttempts = 3, DelayMs = 1)]
    public int FlakyMethod(int succeedOnAttempt)
    {
        CallCount++;
        if (CallCount < succeedOnAttempt)
            throw new InvalidOperationException($"attempt {CallCount} failed");
        return CallCount;
    }
}

public class PollyCircuitBreakerService
{
    public int CallCount;

    [PollyCircuitBreaker(FailureThreshold = 0.5, MinimumThroughput = 2,
                         SamplingDurationSeconds = 30, BreakDurationSeconds = 60)]
    public int AlwaysFails()
    {
        CallCount++;
        throw new InvalidOperationException("always fails");
    }
}

// ── TimeoutHandler ground truth: handler doesn't use any CT internally ──────
//
// Used by Timeout_AbortsToCallerButLeaksTheCall — the body completes in the
// background even though the caller already saw a TimeoutException. This is
// what TimeoutHandler does today (pure Task.WhenAny), and pinning it here
// catches a regression if the handler is ever rewritten to actually cancel.

public class TimeoutNoTokenService
{
    public int CompletedCallCount;

    [Timeout(TimeoutMs = 50)]
    public async Task<int> SlowAsync()
    {
        await Task.Delay(200);
        CompletedCallCount++;
        return 42;
    }
}

// ── AOP0020 ground truth: aspect method passed as delegate ──────────────────

public class DelegateService
{
    public int InvokeCount;

    [Record]
    public int Direct(int id)
    {
        InvokeCount++;
        return id;
    }
}

// ── AOP0021 ground truth: base.Method() call ─────────────────────────────────

public class BaseAspectMethodService
{
    [Record]
    public virtual int Process(int x) => x * 2;
}

#pragma warning disable AOP0021
public class DerivedBaseCallService : BaseAspectMethodService
{
    public int OverrideInvokeCount;

    public override int Process(int x)
    {
        OverrideInvokeCount++;
        // base.Process is what AOP0021 warns about — runtime experiment proved this
        // recurses infinitely (interceptor → @this.Process → override → base → loop).
        return base.Process(x);
    }
}
#pragma warning restore AOP0021

// ── Built-in [Retry] ───────────────────────────────────────────────────────────

public class RetryTestService
{
    public int CallCount { get; private set; }

    [Retry(MaxAttempts = 3)]
    public int FailTwiceThenSucceed()
    {
        CallCount++;
        if (CallCount < 3) throw new InvalidOperationException($"Attempt {CallCount}");
        return 42;
    }

    [Retry(MaxAttempts = 2)]
    public int AlwaysFails()
    {
        CallCount++;
        throw new InvalidOperationException("always");
    }

    [Retry(MaxAttempts = 3, Handle = new[] { typeof(InvalidOperationException) })]
    public int HandleOnly()
    {
        CallCount++;
        if (CallCount == 1) throw new InvalidOperationException("retryable");
        return 1;
    }

    [Retry(MaxAttempts = 3, Handle = new[] { typeof(InvalidOperationException) })]
    public int HandleOnly_WrongException()
    {
        CallCount++;
        throw new ArgumentException("not retryable");
    }

    [Retry(MaxAttempts = 3, Ignore = new[] { typeof(ArgumentException) })]
    public int IgnoreArgException()
    {
        CallCount++;
        if (CallCount == 1) throw new InvalidOperationException("retryable");
        return 2;
    }

    [Retry(MaxAttempts = 3, Ignore = new[] { typeof(ArgumentException) })]
    public int IgnoreArgException_Throws()
    {
        CallCount++;
        throw new ArgumentException("ignored, no retry");
    }
}

// ── Built-in [Cache] ────────────────────────────────────────────────────────────

public class CacheTestService
{
    public int CallCount { get; private set; }

    [Cache(DurationSeconds = 60)]
    public string GetValue(int id)
    {
        CallCount++;
        return $"value-{id}-call-{CallCount}";
    }

    [Cache(DurationSeconds = 60, KeyTemplate = "product:{id}")]
    public string GetWithTemplate(int id, bool includeArchived)
    {
        CallCount++;
        return $"tmpl-{id}-{includeArchived}-call-{CallCount}";
    }

    [Cache(DurationSeconds = 60, KeyTemplate = "nested:{req.CustomerId}:{req.Region}")]
    public string GetWithNestedTemplate(CacheRequest req)
    {
        CallCount++;
        return $"nested-{req.CustomerId}-{req.Region}-call-{CallCount}";
    }
}

public class CacheRequest
{
    public int CustomerId { get; set; }
    public string Region { get; set; } = "";
}

// ── Built-in [Metrics] ──────────────────────────────────────────────────────────

public class MetricsTestService
{
    [Metrics(MetricName = "test.operation")]
    public int Compute(int x) => x * 2;

    [Metrics(MetricName = "test.failing")]
    public int Fail() => throw new InvalidOperationException("boom");
}

// ── Built-in [Timeout] ──────────────────────────────────────────────────────────

public class TimeoutTestService
{
    [Timeout(TimeoutMs = 500)]
    public async Task<int> FastAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        return 42;
    }

    [Timeout(TimeoutMs = 50)]
    public async Task<int> SlowAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(10_000, cancellationToken);
        return 0;
    }
}

// ── Built-in [Authorize] ────────────────────────────────────────────────────────

public class AuthorizeTestService
{
    [Authorize(Roles = "Admin")]
    public async Task<int> AdminOnlyAsync()
    {
        await Task.CompletedTask;
        return 42;
    }

    [Authorize(Policy = "CanEdit")]
    public async Task<int> PolicyProtectedAsync()
    {
        await Task.CompletedTask;
        return 99;
    }

    [Authorize]
    public async Task<int> AuthenticatedOnlyAsync()
    {
        await Task.CompletedTask;
        return 1;
    }
}

// ── Built-in [Validate] ─────────────────────────────────────────────────────────

public class ValidateRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 100)]
    public int Age { get; set; }
}

public class ValidateTestService
{
    [Validate]
    public string Process(ValidateRequest request) => $"ok:{request.Name}";

    [Validate]
    public string ProcessMulti(ValidateRequest request, int count) => $"ok:{count}";
}

// ── Built-in [Transaction] ──────────────────────────────────────────────────────

public class TransactionTestService
{
    public bool Completed { get; private set; }

    [Transaction]
    public int DoWork(int x)
    {
        Completed = System.Transactions.Transaction.Current is not null;
        return x * 2;
    }

    [Transaction]
    public int DoWorkFailing(int x) => throw new InvalidOperationException("rollback");
}
