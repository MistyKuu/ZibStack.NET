using ZibStack.NET.Aop;
using ZibStack.NET.Aop.Tests.Fixtures;
using Xunit;

namespace ZibStack.NET.Aop.Tests;

/// <summary>
/// Stress test for Apply() with complex inheritance, generics, overloads,
/// default interface methods, diamond inheritance, and multi-generic interfaces.
/// All aspects come from Apply() rules (ClassesWhere "E2e*") — no explicit attributes.
/// </summary>
[Collection("Aop")]
public class ComplexInheritanceTests
{
    private readonly RecordingHandler _handler;

    public ComplexInheritanceTests(AopFixture fixture)
    {
        _handler = fixture.Handler;
        _handler.Reset();
    }

    // ── Multi-generic interface: IE2eCanHandle<TCommand, TResult> ──────────

    [Fact]
    public void MultiGeneric_ConcreteCall_HandleCreateOrder()
    {
        var svc = new E2eOrderHandlerImpl();
        var result = svc.Handle(new E2eCreateOrder(5, 2));

        Assert.Equal(50, result.ConfirmationNumber);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Handle"
            && c.Context.ClassName == "E2eOrderHandlerImpl");
    }

    [Fact]
    public void MultiGeneric_ConcreteCall_HandleCancelOrder()
    {
        var svc = new E2eOrderHandlerImpl();
        var result = svc.Handle(new E2eCancelOrder(99));

        Assert.Equal("cancelled-99", result);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Handle"
            && c.Context.ClassName == "E2eOrderHandlerImpl");
    }

    // ── Call through closed generic interface ──────────────────────────────

    [Fact]
    public void ClosedGenericInterface_HandleCreateOrder()
    {
        IE2eCanHandle<E2eCreateOrder, E2eOrderResult> svc = new E2eOrderHandlerImpl();
        var result = svc.Handle(new E2eCreateOrder(3, 1));

        Assert.Equal(30, result.ConfirmationNumber);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Handle");
    }

    [Fact]
    public void ClosedGenericInterface_HandleCancelOrder()
    {
        IE2eCanHandle<E2eCancelOrder> svc = new E2eOrderHandlerImpl();
        var result = svc.Handle(new E2eCancelOrder(7));

        Assert.Equal("cancelled-7", result);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Handle");
    }

    // ── Composed interface (IE2eOrderHandler) ──────────────────────────────

    [Fact]
    public void ComposedInterface_OwnMethod()
    {
        IE2eOrderHandler svc = new E2eOrderHandlerImpl();
        var count = svc.GetPendingCount();

        Assert.Equal(42, count);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetPendingCount");
    }

    [Fact]
    public void ComposedInterface_InheritedHandleMethod()
    {
        IE2eOrderHandler svc = new E2eOrderHandlerImpl();
        var result = svc.Handle(new E2eCreateOrder(1, 1));

        Assert.Equal(10, result.ConfirmationNumber);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Handle");
    }

    // ── Same generic interface, different type args, different impl ────────

    [Fact]
    public void SameInterfaceDifferentTypeArgs_RefundHandler()
    {
        IE2eCanHandle<E2eRefundRequest, E2eRefundResult> svc = new E2eRefundHandler();
        var result = svc.Handle(new E2eRefundRequest(100m, "defective"));

        Assert.Equal(100m, result.RefundedAmount);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Handle");
    }

    [Fact]
    public void SameInterfaceDifferentImpl_CancelOrder()
    {
        // Both E2eOrderHandlerImpl and E2eRefundHandler implement IE2eCanHandle<E2eCancelOrder>
        IE2eCanHandle<E2eCancelOrder> handler1 = new E2eOrderHandlerImpl();
        IE2eCanHandle<E2eCancelOrder> handler2 = new E2eRefundHandler();

        var r1 = handler1.Handle(new E2eCancelOrder(1));
        var r2 = handler2.Handle(new E2eCancelOrder(2));

        Assert.Equal("cancelled-1", r1);
        Assert.Equal("refund-cancel-2", r2);
    }

    // ── Generic base class ─────────────────────────────────────────────────

    [Fact]
    public void GenericBaseClass_OverriddenMethod()
    {
        var svc = new E2eOrderHandlerImpl();
        var desc = svc.Describe();

        Assert.Equal("E2eOrderHandlerImpl", desc);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Describe");
    }

    [Fact]
    public void GenericBaseClass_InheritedMethod()
    {
        var svc = new E2eOrderHandlerImpl();
        var ctx = svc.GetContext();

        Assert.Null(ctx);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetContext");
    }

    // ── Default interface method ───────────────────────────────────────────

    [Fact]
    public void DefaultInterfaceMethod_CalledThroughInterface()
    {
        IE2eAuditable svc = new E2eOrderHandlerImpl();
        var info = svc.GetAuditInfo();

        Assert.Equal("Auditable: OrderHandler", info);
        // DIM calls may or may not be intercepted depending on compiler resolution
        // — the important thing is no build errors
    }

    // ── Overloaded methods on interface ────────────────────────────────────

    [Fact]
    public void Overloads_AllThreeVariants_ConcreteCall()
    {
        var svc = new E2eSearchService();
        var r1 = svc.Search("foo");
        var r2 = svc.Search("bar", 10);
        var r3 = svc.Search("baz", 5, true);

        Assert.Equal("results-foo", r1);
        Assert.Equal("results-bar-max10", r2);
        Assert.Equal("results-baz-max5-archTrue", r3);

        var beforeCalls = _handler.Calls
            .Where(c => c.Phase == "Before" && c.Context.MethodName == "Search")
            .ToList();
        Assert.Equal(3, beforeCalls.Count);
    }

    [Fact]
    public void Overloads_ThroughInterface()
    {
        IE2eSearch svc = new E2eSearchService();
        svc.Search("q1");
        svc.Search("q2", 20);
        svc.Search("q3", 3, false);

        var beforeCalls = _handler.Calls
            .Where(c => c.Phase == "Before" && c.Context.MethodName == "Search")
            .ToList();
        Assert.Equal(3, beforeCalls.Count);
    }

    [Fact]
    public void Overloads_ParameterSignatureCorrect()
    {
        IE2eSearch svc = new E2eSearchService();
        svc.Search("x", 5, true);

        var call = _handler.Calls.First(c =>
            c.Phase == "Before" && c.Context.Parameters.Count == 3);
        Assert.Equal("query", call.Context.Parameters[0].Name);
        Assert.Equal("x", call.Context.Parameters[0].Value);
        Assert.Equal("maxResults", call.Context.Parameters[1].Name);
        Assert.Equal(5, call.Context.Parameters[1].Value);
        Assert.Equal("includeArchived", call.Context.Parameters[2].Name);
        Assert.Equal(true, call.Context.Parameters[2].Value);
    }

    // ── Generic repository ─────────────────────────────────────────────────

    [Fact]
    public void GenericRepo_ConcreteCall()
    {
        var repo = new E2eInMemoryRepository<E2eProduct>();
        repo.Save(new E2eProduct { Name = "Widget", Price = 9.99m });
        var all = repo.FindAll();

        Assert.Single(all);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Save");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "FindAll");
    }

    [Fact]
    public void GenericRepo_ThroughInterface()
    {
        IE2eRepository<E2eProduct> repo = new E2eInMemoryRepository<E2eProduct>();
        repo.Save(new E2eProduct { Name = "Gadget", Price = 19.99m });
        var item = repo.GetById(1);

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Save");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetById");
    }

    // ── Diamond inheritance ────────────────────────────────────────────────

    [Fact]
    public void Diamond_ThroughVersionedInterface()
    {
        IE2eVersioned svc = new E2eDocumentService();
        var name = svc.GetName();    // from IE2eNamed (shared base)
        var ver = svc.GetVersion();  // from IE2eVersioned

        Assert.Equal("document", name);
        Assert.Equal(3, ver);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetName");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetVersion");
    }

    [Fact]
    public void Diamond_ThroughTaggableInterface()
    {
        IE2eTaggable svc = new E2eDocumentService();
        var name = svc.GetName();   // from IE2eNamed (shared base)
        var tags = svc.GetTags();   // from IE2eTaggable

        Assert.Equal("document", name);
        Assert.Equal(2, tags.Length);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetName");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetTags");
    }

    [Fact]
    public void Diamond_ThroughBaseInterface()
    {
        IE2eNamed svc = new E2eDocumentService();
        var name = svc.GetName();

        Assert.Equal("document", name);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "GetName");
    }

    [Fact]
    public void Diamond_AllInterfacesInOneTest()
    {
        var doc = new E2eDocumentService();
        IE2eNamed named = doc;
        IE2eVersioned versioned = doc;
        IE2eTaggable taggable = doc;

        named.GetName();
        versioned.GetVersion();
        taggable.GetTags();
        doc.GetName(); // concrete call

        var beforeCalls = _handler.Calls.Where(c => c.Phase == "Before").ToList();
        Assert.Equal(4, beforeCalls.Count);
    }

    // ── Mix: concrete + multiple interface refs to same object ─────────────

    [Fact]
    public void MixedCallSites_SameObject_DifferentRefs()
    {
        var impl = new E2eOrderHandlerImpl();
        IE2eOrderHandler orderHandler = impl;
        IE2eCanHandle<E2eCreateOrder, E2eOrderResult> createHandler = impl;
        IE2eCanHandle<E2eCancelOrder> cancelHandler = impl;
        IE2eAuditable auditable = impl;

        impl.GetPendingCount();                        // concrete
        orderHandler.GetPendingCount();                // composed interface
        createHandler.Handle(new E2eCreateOrder(1, 1)); // closed generic
        cancelHandler.Handle(new E2eCancelOrder(1));    // another closed generic
        impl.Describe();                                // overridden from base
        impl.GetContext();                              // inherited from base

        var beforeCalls = _handler.Calls.Where(c => c.Phase == "Before").ToList();
        Assert.True(beforeCalls.Count >= 6, $"Expected >= 6, got {beforeCalls.Count}");
    }
}
