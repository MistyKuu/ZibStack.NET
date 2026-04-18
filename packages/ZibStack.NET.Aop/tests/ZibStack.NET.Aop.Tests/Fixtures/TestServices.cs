using ZibStack.NET.Aop;
using ZibStack.NET.Log;

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

// ── TimeoutHandler ground truths: cooperative vs non-cooperative paths ──────
//
// AOP0015 fires on TimeoutNoTokenService (no CT param) — pragma-suppress so the
// project compiles; the test asserts the analyzer's claim is true at runtime
// (body keeps running in background after caller sees TimeoutException).
//
// TimeoutWithTokenService verifies the cooperative path the rewritten handler
// takes when the method DOES have a CT param: TimeoutHandler.CancelAfter signals
// the linked CTS the generator wired in, the body's Task.Delay observes it,
// the body throws OperationCanceledException → handler translates to
// TimeoutException, and the body does NOT complete in background.

#pragma warning disable AOP0015
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
#pragma warning restore AOP0015

public class TimeoutWithTokenService
{
    public int CompletedCallCount;

    [Timeout(TimeoutMs = 50)]
    public async Task<int> SlowAsync(CancellationToken cancellationToken = default)
    {
        // Forward the token to the inner await — when TimeoutHandler signals the
        // linked CTS via CancelAfter, this Task.Delay throws TaskCanceledException
        // and the body never reaches CompletedCallCount++.
        await Task.Delay(200, cancellationToken);
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

// ── Built-in [Debounce] ─────────────────────────────────────────────────────────

public class DebounceTestService
{
    public int CallCount;

    [Debounce(DelayMs = 100)]
    public async Task<int> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CallCount);
        await Task.Delay(1, cancellationToken);
        return query.Length;
    }
}

// ── Built-in [Throttle] ─────────────────────────────────────────────────────────

public class ThrottleTestService
{
    public int CallCount;

    [Throttle(IntervalMs = 200, Trailing = false)]
    public async Task<int> NotifyAsync(string userId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CallCount);
        await Task.Delay(1, cancellationToken);
        return CallCount;
    }

    [Throttle(IntervalMs = 200, Trailing = true)]
    public async Task<int> NotifyTrailingAsync(string userId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CallCount);
        await Task.Delay(1, cancellationToken);
        return CallCount;
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


// ── [Audit] test service + in-memory store ─────────────────────────────────

public class InMemoryAuditStore : IAuditStore
{
    public List<AuditEntry> Entries { get; } = new();
    public System.Threading.Tasks.Task WriteAsync(AuditEntry entry, System.Threading.CancellationToken ct = default)
    {
        Entries.Add(entry);
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

public class AuditTestService
{
    [Audit]
    public string UpdateName(int id, string newName) => $"updated-{id}-{newName}";

    [Audit(Action = "PlaceOrder")]
    public int PlaceOrder(string customer, decimal total) => 42;

    [Audit]
    public void FailingMethod() => throw new InvalidOperationException("boom");

    [Audit]
    public string SensitiveMethod(int id, [Sensitive] string secret) => "ok";
}

// ── Apply() bulk aspect test services ──────────────────────────────────────────

// Interface used by Apply().Implementing<IApplyTarget>()
public interface IApplyTarget
{
    int GetValue(int x);
}

// NO aspect attributes — aspects are applied via IAopConfigurator.Apply()
public class ApplyTargetService : IApplyTarget
{
    public int CallCount;

    public int GetValue(int x)
    {
        Interlocked.Increment(ref CallCount);
        return x * 2;
    }

    public int InternalMethod(int x) => x + 1;
}

// Service NOT implementing IApplyTarget — should NOT get the aspect
public class NonTargetService
{
    public int CallCount;

    public int GetValue(int x)
    {
        Interlocked.Increment(ref CallCount);
        return x * 3;
    }
}

// Base class for DerivedFrom tests
public abstract class BaseApplyService
{
    public abstract int Compute(int x);
}

public class DerivedApplyService : BaseApplyService
{
    public int CallCount;
    public override int Compute(int x)
    {
        Interlocked.Increment(ref CallCount);
        return x * 10;
    }
}

// Class name predicate test
public class OrderProcessor
{
    public int CallCount;
    public int Handle(int x)
    {
        Interlocked.Increment(ref CallCount);
        return x;
    }
}

// Excluded class
public class ExcludedApplyService : IApplyTarget
{
    public int CallCount;
    public int GetValue(int x)
    {
        Interlocked.Increment(ref CallCount);
        return x * 5;
    }
}

// Configurator with multiple Apply rules
public sealed class ApplyTestConfig : IAopConfigurator
{
    public void Configure(IAopBuilder b)
    {
        // Rule 1: Cache all public methods on IApplyTarget implementations, except ExcludedApplyService
        b.Apply<CacheAttribute>(to => to
            .Implementing<IApplyTarget>()
            .PublicMethods()
            .Except<ExcludedApplyService>()
        );

        // Rule 2: Retry on DerivedFrom<BaseApplyService>
        b.Apply<RetryAttribute>(to => to
            .DerivedFrom<BaseApplyService>()
        , r => r.MaxAttempts = 2);

        // Rule 3: Cache on classes whose name starts with "Order"
        b.Apply<CacheAttribute>(to => to
            .ClassesWhere(c => c.Name.StartsWith("Order"))
        );
    }
}
