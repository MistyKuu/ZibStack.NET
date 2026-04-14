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
    public async Task<int> FastAsync()
    {
        await Task.Delay(1);
        return 42;
    }

    [Timeout(TimeoutMs = 50)]
    public async Task<int> SlowAsync()
    {
        await Task.Delay(10_000);
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
