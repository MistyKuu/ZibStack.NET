using System.Diagnostics;
using System.Diagnostics.Metrics;
using ZibStack.NET.Aop;
using ZibStack.NET.Aop.Tests.Fixtures;
using Xunit;

namespace ZibStack.NET.Aop.Tests;

/// <summary>
/// E2E showcase: verifies that global <c>Apply()</c> rules fire Trace spans,
/// Metrics counters, and aspect handlers at runtime — across multiple plain
/// services that carry NO aspect attributes. Everything is wired via
/// <see cref="ApplyTestConfig"/> rules 4-6 targeting <c>E2e*</c> classes.
/// </summary>
[Collection("Aop")]
public class GlobalAopE2eTests : IDisposable
{
    private readonly RecordingHandler _handler;

    public GlobalAopE2eTests(AopFixture fixture)
    {
        _handler = fixture.Handler;
        _handler.Reset();
    }

    public void Dispose() => _handler.Reset();

    // ── Record handler (Before/After lifecycle) ────────────────────────────

    [Fact]
    public void GlobalApply_MultipleServices_RecordHandlerFires()
    {
        var orders = new E2eOrderService();
        var inventory = new E2eInventoryService();

        orders.PlaceOrder("Alice", 99.99m);
        inventory.CheckStock("SKU-001");

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.ClassName == "E2eOrderService" && c.Context.MethodName == "PlaceOrder");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "After" && c.Context.ClassName == "E2eOrderService" && c.Context.MethodName == "PlaceOrder");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.ClassName == "E2eInventoryService" && c.Context.MethodName == "CheckStock");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "After" && c.Context.ClassName == "E2eInventoryService" && c.Context.MethodName == "CheckStock");
    }

    [Fact]
    public async Task GlobalApply_AsyncMethod_RecordHandlerFires()
    {
        var inventory = new E2eInventoryService();
        var result = await inventory.ReserveAsync("SKU-002", 5);

        Assert.True(result);
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "ReserveAsync");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "After" && c.Context.MethodName == "ReserveAsync");
    }

    // ── Trace (Activity spans) ─────────────────────────────────────────────

    [Fact]
    public void GlobalApply_Trace_CreatesActivitySpans()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name is "E2eOrderService" or "E2eInventoryService",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var orders = new E2eOrderService();
        orders.PlaceOrder("Bob", 42.00m);
        orders.GetStatus(7);

        var inventory = new E2eInventoryService();
        inventory.CheckStock("SKU-003");

        Assert.Equal(3, activities.Count);

        var placeOrder = activities.First(a => a.OperationName == "PlaceOrder");
        Assert.Equal("E2eOrderService", placeOrder.Source.Name);
        Assert.Equal("E2eOrderService", placeOrder.GetTagItem("code.namespace"));
        Assert.Equal("PlaceOrder", placeOrder.GetTagItem("code.function"));
        Assert.Equal("Bob", placeOrder.GetTagItem("customer"));

        var getStatus = activities.First(a => a.OperationName == "GetStatus");
        Assert.Equal("7", getStatus.GetTagItem("orderId"));

        var checkStock = activities.First(a => a.OperationName == "CheckStock");
        Assert.Equal("E2eInventoryService", checkStock.Source.Name);
    }

    // ── Metrics (call count + duration) ────────────────────────────────────

    [Fact]
    public void GlobalApply_Metrics_RecordsCallCountAndDuration()
    {
        long callCount = 0;
        var durations = new List<double>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Meter.Name != MetricsHandler.DefaultMeterName) return;
            if (instrument.Name is "aop.method.call.count" or "aop.method.call.duration")
                ml.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "aop.class" && tag.Value is "E2eOrderService")
                { callCount += measurement; return; }
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "aop.class" && tag.Value is "E2eOrderService")
                { durations.Add(measurement); return; }
        });
        meterListener.Start();

        var svc = new E2eOrderService();
        svc.PlaceOrder("Carol", 10.00m);
        svc.GetStatus(3);

        Assert.Equal(2, callCount);
        Assert.Equal(2, durations.Count);
        Assert.All(durations, d => Assert.True(d >= 0));
    }

    // ── Control group (no interception) ────────────────────────────────────

    [Fact]
    public void GlobalApply_ControlGroup_NotIntercepted()
    {
        var svc = new ControlGroupService();
        svc.DoWork(42);

        Assert.DoesNotContain(_handler.Calls, c =>
            c.Context.ClassName == "ControlGroupService");
    }

    // ── Context carries correct parameter metadata ─────────────────────────

    [Fact]
    public void GlobalApply_ContextHasParameters()
    {
        var svc = new E2eOrderService();
        svc.PlaceOrder("Dave", 55.50m);

        var ctx = _handler.Calls
            .First(c => c.Phase == "Before" && c.Context.MethodName == "PlaceOrder")
            .Context;

        Assert.Equal("E2eOrderService", ctx.ClassName);
        Assert.Equal("PlaceOrder", ctx.MethodName);
        Assert.Contains(ctx.Parameters, p => p.Name == "customer" && (string)p.Value! == "Dave");
        Assert.Contains(ctx.Parameters, p => p.Name == "total" && (decimal)p.Value! == 55.50m);
    }

    // ── All three aspects fire together ────────────────────────────────────

    [Fact]
    public void GlobalApply_FullStack_TraceMetricsAndRecordAllFire()
    {
        // Trace
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "E2eOrderService",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(activityListener);

        // Metrics
        long metricsCalls = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Meter.Name == MetricsHandler.DefaultMeterName
                && instrument.Name == "aop.method.call.count")
                ml.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "aop.class" && tag.Value is "E2eOrderService")
                { metricsCalls += measurement; return; }
        });
        meterListener.Start();

        // One call — all three layers should fire
        var svc = new E2eOrderService();
        svc.PlaceOrder("Eve", 100m);

        // Record handler
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "PlaceOrder");
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "After" && c.Context.MethodName == "PlaceOrder");

        // Trace span
        Assert.Single(activities);
        Assert.Equal("PlaceOrder", activities[0].OperationName);
        Assert.Equal(ActivityStatusCode.Ok, activities[0].Status);

        // Metrics counter
        Assert.Equal(1, metricsCalls);
    }

    // ── Concrete class call + interface call on the same type ────────────

    [Fact]
    public void GlobalApply_ConcreteCall_RecordFires()
    {
        var svc = new E2eSimpleHandler();
        svc.Execute();

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Execute"
            && c.Context.ClassName == "E2eSimpleHandler");
    }

    [Fact]
    public void GlobalApply_InterfaceCall_RecordFires()
    {
        IE2eHandler svc = new E2eSimpleHandler();
        svc.Execute();

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Execute"
            && c.Context.ClassName == "IE2eHandler");
    }

    [Fact]
    public void GlobalApply_BothConcreteAndInterface_CoexistWithoutCollision()
    {
        var concrete = new E2eSimpleHandler();
        IE2eHandler iface = new E2eSimpleHandler();

        concrete.Execute();
        iface.Execute();

        // Concrete call → ClassName == "E2eSimpleHandler"
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.ClassName == "E2eSimpleHandler");
        // Interface call → ClassName == "IE2eHandler"
        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.ClassName == "IE2eHandler");
    }

    // ── Dual-source: explicit [Record] on interface + Apply rules on impl ─

    [Fact]
    public void DualSource_ExplicitOnInterface_PlusApplyOnImpl_NoDuplicate()
    {
        // IE2eDualSource has explicit [Record] AND E2eDualSourceImpl matches Apply rules
        // (ClassesWhere "E2e" applies Trace/Metrics/Record). Deduplication prevents CS9153.
        IE2eDualSource svc = new E2eDualSourceImpl();
        svc.Process(42);

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Process");
    }

    [Fact]
    public void DualSource_ConcreteCall_StillWorks()
    {
        var svc = new E2eDualSourceImpl();
        svc.Process(7);

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Process");
    }

    // ── Method overloads (same name, different params) ───────────────────

    [Fact]
    public void GlobalApply_Overloads_AllIntercepted()
    {
        IE2eHandler svc = new E2eSimpleHandler();
        svc.Execute();
        svc.Execute(42);
        svc.Execute("test", 3);

        var beforeCalls = _handler.Calls
            .Where(c => c.Phase == "Before" && c.Context.MethodName == "Execute")
            .ToList();
        Assert.Equal(3, beforeCalls.Count);
    }

    [Fact]
    public void GlobalApply_Overloads_ParametersCorrect()
    {
        IE2eHandler svc = new E2eSimpleHandler();
        svc.Execute(42);
        svc.Execute("hello", 7);

        var call1 = _handler.Calls.First(c =>
            c.Phase == "Before" && c.Context.Parameters.Count == 1);
        Assert.Equal("id", call1.Context.Parameters[0].Name);
        Assert.Equal(42, call1.Context.Parameters[0].Value);

        var call2 = _handler.Calls.First(c =>
            c.Phase == "Before" && c.Context.Parameters.Count == 2);
        Assert.Equal("name", call2.Context.Parameters[0].Name);
        Assert.Equal("hello", call2.Context.Parameters[0].Value);
        Assert.Equal("count", call2.Context.Parameters[1].Name);
        Assert.Equal(7, call2.Context.Parameters[1].Value);
    }

    // ── Generic arity collision: IHandler and IHandler<T> ──────────────────

    [Fact]
    public void GlobalApply_NonGenericInterface_RecordFires()
    {
        IE2eHandler svc = new E2eSimpleHandler();
        svc.Execute();

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Execute"
            && c.Context.ClassName == "IE2eHandler");
    }

    [Fact]
    public void GlobalApply_GenericInterface_RecordFires()
    {
        IE2eHandler<string> svc = new E2eGenericHandler<string>();
        svc.Fetch(42);

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Fetch"
            && c.Context.ClassName == "IE2eHandler");
    }

    [Fact]
    public void GlobalApply_BaseMethodCalledThroughGenericInterface_RecordFires()
    {
        // Call Execute() (declared on IE2eHandler) through IE2eHandler<T> reference
        IE2eHandler<string> svc = new E2eGenericHandler<string>();
        svc.Execute(); // inherited from IE2eHandler

        Assert.Contains(_handler.Calls, c =>
            c.Phase == "Before" && c.Context.MethodName == "Execute"
            && c.Context.ClassName == "IE2eHandler");
    }

    [Fact]
    public void GlobalApply_BothArities_CoexistWithoutCollision()
    {
        IE2eHandler simple = new E2eSimpleHandler();
        IE2eHandler<string> generic = new E2eGenericHandler<string>();

        simple.Execute();
        generic.Fetch(1);

        // Both should fire — proves no duplicate class name collision
        var beforeCalls = _handler.Calls.Where(c => c.Phase == "Before").ToList();
        Assert.True(beforeCalls.Count >= 2);
    }
}
