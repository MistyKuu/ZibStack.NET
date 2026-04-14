using ZibStack.NET.Log;

namespace ZibStack.NET.Log.Tests.Fixtures;

// ── Method-level [Log] ──────────────────────────────────────────────────────

public class SimpleLogService
{
    [Log]
    public int Add(int a, int b) => a + b;

    public int Plain(int x) => x; // no [Log]
}

// ── Class-level [Log] ───────────────────────────────────────────────────────

[Log]
public class ClassLogService
{
    public int Multiply(int a, int b) => a * b;
    public string Echo(string msg) => msg;
}

// ── Async ────────────────────────────────────────────────────────────────────

public class AsyncLogService
{
    [Log]
    public async Task<int> ComputeAsync(int x)
    {
        await Task.Delay(1);
        return x * 2;
    }
}

// ── Exception ────────────────────────────────────────────────────────────────

public class ThrowingLogService
{
    [Log]
    public int Fail() => throw new InvalidOperationException("boom");
}

// ── [Sensitive] parameter ────────────────────────────────────────────────────

public class SensitiveLogService
{
    [Log]
    public string Login(string user, [Sensitive] string password) => $"ok:{user}";
}

// ── Interface + class-level [Log] ────────────────────────────────────────────

public interface ILoggedOrderService
{
    int GetOrder(int id);
}

[Log]
public class LoggedOrderServiceImpl : ILoggedOrderService
{
    public int GetOrder(int id) => id;
}

// ── Interface + method-level [Log] on impl (not interface) ───────────────────

public interface ISelectiveLogService
{
    int Tracked(int x);
    int Untracked(int x);
}

public class SelectiveLogServiceImpl : ISelectiveLogService
{
    [Log]
    public int Tracked(int x) => x;

    public int Untracked(int x) => x; // no [Log]
}

// ── Generic class with [Log] ─────────────────────────────────────────────────

[Log]
public class GenericLogRepo<T> where T : class
{
    public T? Get(int id) => default;
}

// ── Generic method with [Log] ────────────────────────────────────────────────

public class GenericLogMethodService
{
    [Log]
    public T? Fetch<T>(int id) where T : class => default;
}

// ── Inheritance ──────────────────────────────────────────────────────────────

[Log]
public class BaseLogService
{
    public virtual int Process(int x) => x * 2;
}

public class DerivedLogService : BaseLogService { }
