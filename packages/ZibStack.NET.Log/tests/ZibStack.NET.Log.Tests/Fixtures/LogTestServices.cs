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

// ── Apply() bulk [Log] test services ────────────────────────────────────────
// NO [Log] attributes — logging applied via IAopConfigurator.Apply()

public interface IE2ePaymentService
{
    string Charge(string customer, decimal amount);
    int GetBalance(int accountId);
}

public class E2ePaymentService : IE2ePaymentService
{
    public int CallCount;

    public string Charge(string customer, decimal amount)
    {
        Interlocked.Increment(ref CallCount);
        return $"charged-{customer}-{amount}";
    }

    public int GetBalance(int accountId)
    {
        Interlocked.Increment(ref CallCount);
        return accountId * 100;
    }
}

public class E2eShippingService
{
    public int CallCount;

    public string Ship(string address)
    {
        Interlocked.Increment(ref CallCount);
        return $"shipped-{address}";
    }

    public async Task<bool> TrackAsync(string trackingId)
    {
        Interlocked.Increment(ref CallCount);
        await Task.Delay(1);
        return trackingId.Length > 0;
    }
}

public class E2eThrowingService
{
    public int Explode() => throw new InvalidOperationException("kaboom");
}

// Control group — name does NOT start with "E2e"
public class PlainUninstrumentedService
{
    public int DoWork(int x) => x;
}

// Configurator applying [Log] to all E2e* classes
public sealed class LogApplyTestConfig : ZibStack.NET.Aop.IAopConfigurator
{
    public void Configure(ZibStack.NET.Aop.IAopBuilder b)
    {
        b.Apply<LogAttribute>(to => to
            .ClassesWhere(c => c.Name.StartsWith("E2e"))
            .PublicMethods()
        );

        // Same class-matcher style but with a configure lambda — exercises the
        // "Apply(selector, configure)" overload and verifies that the aspect
        // property (LogParameters=false) flows down into the generated interceptor.
        b.Apply<LogAttribute>(
            to => to.ClassesWhere(c => c.Name.StartsWith("NoParams")),
            a => a.LogParameters = false
        );
    }
}

// Service whose params should NOT appear in the entry log, because the Apply rule
// carries `a => a.LogParameters = false`. Covers hierarchy level 3 (Apply-config).
public class NoParamsService
{
    public string Charge(string customer, decimal amount) => $"charged-{customer}-{amount}";
}

// ── [NoLog] on parameters via interface dispatch ────────────────────────────
// Two shapes: [NoLog] declared on the interface parameter only, and [NoLog]
// declared on the impl parameter only. Both should redact the value when the
// call is made through the interface (i.e. goes through the synthesized proxy).

public interface INoLogIfaceParamService
{
    string DoThing(string visible, [NoLog] string secret);
}

public class NoLogIfaceParamServiceImpl : INoLogIfaceParamService
{
    [Log]
    public string DoThing(string visible, string secret) => $"{visible}-{secret}";
}

public interface INoLogImplParamService
{
    string DoThing(string visible, string secret);
}

public class NoLogImplParamServiceImpl : INoLogImplParamService
{
    [Log]
    public string DoThing(string visible, [NoLog] string secret) => $"{visible}-{secret}";
}
