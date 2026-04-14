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
