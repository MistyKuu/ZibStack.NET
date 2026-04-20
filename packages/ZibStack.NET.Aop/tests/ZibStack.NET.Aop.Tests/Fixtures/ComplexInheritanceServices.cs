using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Tests.Fixtures;

// ══════════════════════════════════════════════════════════════════════════════
// Complex inheritance / generic / overload / DIM stress test for Apply() + AOP
// ══════════════════════════════════════════════════════════════════════════════

// ── Base interfaces ────────────────────────────────────────────────────────

public interface IE2eCanHandle<TCommand>
{
    string Handle(TCommand command);
}

public interface IE2eCanHandle<TCommand, TResult>
{
    TResult Handle(TCommand command);
}

// ── Composed interface inheriting multiple closed generics ─────────────────

public interface IE2eOrderHandler
    : IE2eCanHandle<E2eCreateOrder, E2eOrderResult>,
      IE2eCanHandle<E2eCancelOrder>
{
    // Own method
    int GetPendingCount();
}

// ── Interface with default implementation ──────────────────────────────────

public interface IE2eAuditable
{
    string AuditName { get; }

    // Default interface method
    string GetAuditInfo() => $"Auditable: {AuditName}";
}

// ── Generic base class ────────────────────────────────────────────────────

public abstract class E2eBaseService<TContext> where TContext : class
{
    public int BaseCallCount;

    public virtual string Describe()
    {
        Interlocked.Increment(ref BaseCallCount);
        return $"Base<{typeof(TContext).Name}>";
    }

    public TContext? GetContext() => default;
}

// ── Concrete handler: inherits generic base, implements multi-generic iface + DIM ──

public class E2eOrderHandlerImpl
    : E2eBaseService<E2eOrderContext>,
      IE2eOrderHandler,
      IE2eAuditable
{
    public int CallCount;

    public string AuditName => "OrderHandler";

    // IE2eCanHandle<E2eCreateOrder, E2eOrderResult>
    public E2eOrderResult Handle(E2eCreateOrder command)
    {
        Interlocked.Increment(ref CallCount);
        return new E2eOrderResult(command.ProductId * 10);
    }

    // IE2eCanHandle<E2eCancelOrder> (returns string)
    public string Handle(E2eCancelOrder command)
    {
        Interlocked.Increment(ref CallCount);
        return $"cancelled-{command.OrderId}";
    }

    // IE2eOrderHandler.GetPendingCount
    public int GetPendingCount()
    {
        Interlocked.Increment(ref CallCount);
        return 42;
    }

    // Override base
    public override string Describe()
    {
        Interlocked.Increment(ref BaseCallCount);
        return "E2eOrderHandlerImpl";
    }
}

// ── Second handler implementing same generic interface with different T ────

public class E2eRefundHandler
    : IE2eCanHandle<E2eRefundRequest, E2eRefundResult>,
      IE2eCanHandle<E2eCancelOrder>
{
    public int CallCount;

    public E2eRefundResult Handle(E2eRefundRequest command)
    {
        Interlocked.Increment(ref CallCount);
        return new E2eRefundResult(command.Amount);
    }

    public string Handle(E2eCancelOrder command)
    {
        Interlocked.Increment(ref CallCount);
        return $"refund-cancel-{command.OrderId}";
    }
}

// ── Interface with overloaded methods ──────────────────────────────────────

public interface IE2eSearch
{
    string Search(string query);
    string Search(string query, int maxResults);
    string Search(string query, int maxResults, bool includeArchived);
}

public class E2eSearchService : IE2eSearch
{
    public int CallCount;

    public string Search(string query)
    {
        Interlocked.Increment(ref CallCount);
        return $"results-{query}";
    }

    public string Search(string query, int maxResults)
    {
        Interlocked.Increment(ref CallCount);
        return $"results-{query}-max{maxResults}";
    }

    public string Search(string query, int maxResults, bool includeArchived)
    {
        Interlocked.Increment(ref CallCount);
        return $"results-{query}-max{maxResults}-arch{includeArchived}";
    }
}

// ── Generic repo with constraints + interface ─────────────────────────────

public interface IE2eRepository<T> where T : class, new()
{
    T? GetById(int id);
    void Save(T entity);
    List<T> FindAll();
}

public class E2eInMemoryRepository<T> : IE2eRepository<T> where T : class, new()
{
    public int CallCount;
    private readonly Dictionary<int, T> _store = new();

    public T? GetById(int id)
    {
        Interlocked.Increment(ref CallCount);
        return _store.GetValueOrDefault(id);
    }

    public void Save(T entity)
    {
        Interlocked.Increment(ref CallCount);
        _store[_store.Count + 1] = entity;
    }

    public List<T> FindAll()
    {
        Interlocked.Increment(ref CallCount);
        return _store.Values.ToList();
    }
}

// ── Diamond inheritance ───────────────────────────────────────────────────

public interface IE2eNamed
{
    string GetName();
}

public interface IE2eVersioned : IE2eNamed
{
    int GetVersion();
}

public interface IE2eTaggable : IE2eNamed
{
    string[] GetTags();
}

// Implements both IE2eVersioned and IE2eTaggable which both inherit IE2eNamed
public class E2eDocumentService : IE2eVersioned, IE2eTaggable
{
    public int CallCount;

    public string GetName()
    {
        Interlocked.Increment(ref CallCount);
        return "document";
    }

    public int GetVersion()
    {
        Interlocked.Increment(ref CallCount);
        return 3;
    }

    public string[] GetTags()
    {
        Interlocked.Increment(ref CallCount);
        return new[] { "tag1", "tag2" };
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────

public record E2eCreateOrder(int ProductId, int Quantity);
public record E2eCancelOrder(int OrderId);
public record E2eOrderResult(int ConfirmationNumber);
public record E2eRefundRequest(decimal Amount, string Reason);
public record E2eRefundResult(decimal RefundedAmount);
public record E2eOrderContext;
public class E2eProduct
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
