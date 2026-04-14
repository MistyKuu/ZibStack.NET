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
