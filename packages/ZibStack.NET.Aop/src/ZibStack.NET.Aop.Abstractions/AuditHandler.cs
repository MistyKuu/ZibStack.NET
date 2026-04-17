using System;
using System.Collections.Generic;

namespace ZibStack.NET.Aop;

/// <summary>
/// Runtime handler for <see cref="AuditAttribute"/>. Snapshots method parameters
/// before and after execution, then writes an <see cref="AuditEntry"/> to the
/// registered <see cref="IAuditStore"/>. No-ops silently when no store is registered.
/// </summary>
public sealed class AuditHandler : IAspectHandler
{
    private const string BeforeKey = "__audit_before";
    private const string ActionKey = "__audit_action";

    private readonly IAuditStore? _store;

    public AuditHandler(IServiceProvider sp)
    {
        _store = sp.GetService(typeof(IAuditStore)) as IAuditStore;
    }

    public void OnBefore(AspectContext context)
    {
        if (_store is null) return;

        context.Properties[BeforeKey] = SnapshotParameters(context);

        var action = context.MethodName;
        foreach (var p in context.Parameters)
        {
            if (p.Name == "__audit_action" && p.Value is string a)
            { action = a; break; }
        }
        context.Properties[ActionKey] = action;
    }

    public void OnAfter(AspectContext context)
    {
        if (_store is null) return;

        var before = context.Properties.TryGetValue(BeforeKey, out var b) ? b as string : null;
        var action = context.Properties.TryGetValue(ActionKey, out var a) ? a as string ?? context.MethodName : context.MethodName;

        // Fire-and-forget: audit writes shouldn't block the request pipeline.
        // If the store throws, it's a background failure — the store impl should
        // handle its own error logging.
        _ = _store.WriteAsync(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            ClassName = context.ClassName,
            MethodName = context.MethodName,
            Action = action,
            BeforeSnapshot = before,
            AfterSnapshot = SnapshotParameters(context),
            ResultSummary = context.ReturnValue?.ToString(),
            ElapsedMs = context.ElapsedMilliseconds,
        });
    }

    public void OnException(AspectContext context, Exception exception)
    {
        if (_store is null) return;

        var before = context.Properties.TryGetValue(BeforeKey, out var b) ? b as string : null;
        var action = context.Properties.TryGetValue(ActionKey, out var a) ? a as string ?? context.MethodName : context.MethodName;

        _ = _store.WriteAsync(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            ClassName = context.ClassName,
            MethodName = context.MethodName,
            Action = action,
            BeforeSnapshot = before,
            IsError = true,
            ExceptionType = exception.GetType().FullName,
            ExceptionMessage = exception.Message,
            ElapsedMs = context.ElapsedMilliseconds,
        });
    }

    private static string? SnapshotParameters(AspectContext context)
    {
        if (context.Parameters.Count == 0) return null;

        var parts = new List<string>();
        foreach (var p in context.Parameters)
        {
            if (p.IsNoLog) continue;
            var val = p.IsSensitive ? "***" : (p.Value?.ToString() ?? "null");
            parts.Add($"{p.Name}={val}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
