using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using ZibStack.NET.Aop.Generator;

namespace ZibStack.NET.Log.Generator;

/// <summary>
/// Compile-time inline emitter for the [Log] aspect.
/// Full parity with the old ZibLogEmitter: LoggerMessage.Define,
/// ObjectLogMode (Destructure/Json/ToString), [Sensitive]/[NoLog] on params/return,
/// optional Stopwatch, custom messages.
/// </summary>
internal sealed class LogAspectEmitter : IAspectEmitter
{
    public string AttributeFullName => "ZibStack.NET.Log.LogAttribute";

    private static readonly string[] LogLevelNames =
        { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" };

    // Per-class emission state. Reset in BeginClass() so incremental re-runs of the
    // generator don't leak state across classes or re-emissions. The previous design used
    // a HashSet<string> keyed by class name that survived across runs, which caused the
    // shared __cachedLogger field to be silently dropped on the second run.
    private bool _classMembersEmitted;
    private int _eventId;

    public IEnumerable<string> RequiredUsings => new[] { "Microsoft.Extensions.Logging" };

    public void BeginClass(InterceptedClassModel classModel)
    {
        _classMembersEmitted = false;
        _eventId = 1;
    }

    // === EmitClassMembers: LoggerMessage.Define delegates ===

    public void EmitClassMembers(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        if (!_classMembersEmitted)
        {
            _classMembersEmitted = true;
            // Cached DI-resolved logger — resolved once per class.
            // Generic classes can't share a single ILogger<T> across different closed
            // constructions (different T → different ILogger<T>), so we skip the cache
            // and resolve inline for generic types.
            if (cls.TypeParameters.Count == 0)
            {
                sb.AppendLine($"{indent}private static global::Microsoft.Extensions.Logging.ILogger? __cachedLogger;");
                sb.AppendLine();
            }

            // Emit sanitizer methods for types with [Sensitive]/[NoLog] properties
            var emittedSanitizers = new HashSet<string>();
            foreach (var m in cls.Methods)
            {
                foreach (var p in m.Parameters)
                    if (p.SanitizedType != null) EmitSanitizer(sb, p.SanitizedType, emittedSanitizers, indent);
                if (m.SanitizedReturnType != null) EmitSanitizer(sb, m.SanitizedReturnType, emittedSanitizers, indent);
            }
        }

        var level = P(aspect, "EntryExitLevel", 2, cls);
        var exLevel = P(aspect, "ExceptionLevel", 4, cls);
        var logParams = P(aspect, "LogParameters", true, cls);
        var logReturn = P(aspect, "LogReturnValue", true, cls) && !aspect.NoLogReturn;
        var measureElapsed = P(aspect, "MeasureElapsed", true, cls);
        var objectLogging = P(aspect, "ObjectLogging", 1, cls);

        var loggable = logParams ? method.Parameters.Where(p => !p.IsNoLog).ToList()
            : new List<InterceptedParameterModel>();

        // Generic methods declare their own type parameters (e.g. `T Create<T>()`), and a
        // static `LoggerMessage.Define<T>` delegate cached on the non-generic wrapper class
        // cannot reference those method-level parameters — the type simply isn't in scope.
        // The same applies when the containing class is generic (`BaseService<T>`) and a
        // method's parameters/return type reference the class-level T: the wrapper
        // `__BaseService_Aop` is NOT generic, so T isn't visible there either.
        // Fall back to inline `__logger.Log(...)` in both cases (see EmitBefore).
        if (UseInlinePath(method, loggable.Count, cls))
            return;

        // --- Entry delegate ---
        var entryMsg = PStr(aspect, "EntryMessage") ?? BuildEntryMessage(cls, method, loggable, objectLogging);
        if (loggable.Count <= 6)
        {
            var types = loggable.Select(p => GetParamLogType(p, objectLogging)).ToList();
            EmitDelegate(sb, indent, method.MethodName, "Entry", types, level, ref _eventId, entryMsg);
        }
        else _eventId++;

        // --- Exit delegate ---
        var exitTypes = new List<string>();
        if (measureElapsed) exitTypes.Add("long");
        if (logReturn && !method.ReturnsVoid)
        {
            if (method.SanitizedReturnType != null && objectLogging == 1)
                exitTypes.Add("object"); // Destructure + sanitized → dict as object
            else if (objectLogging == 1 && method.HasComplexReturnType)
                exitTypes.Add("object"); // Destructure + complex → object
            else
                exitTypes.Add("string?");
        }
        var exitMsg = PStr(aspect, "ExitMessage") ?? BuildExitMessage(cls, method, measureElapsed, logReturn, objectLogging);
        EmitDelegate(sb, indent, method.MethodName, "Exit", exitTypes, level, ref _eventId, exitMsg);

        // --- Error delegate ---
        var errTypes = measureElapsed ? new List<string> { "long" } : new List<string>();
        var errMsg = PStr(aspect, "ExceptionMessage") ?? BuildErrorMessage(cls, method, measureElapsed);
        EmitDelegate(sb, indent, method.MethodName, "Error", errTypes, exLevel, ref _eventId, errMsg);
    }

    /// <summary>
    /// True when we must use the inline <c>__logger.Log(...)</c> path (no pre-baked
    /// <c>LoggerMessage.Define</c> delegate): too many params, method declares its own
    /// type parameters, or the containing class is itself generic (delegate fields live
    /// on a non-generic wrapper, so any reference to a class/method type parameter is
    /// out of scope).
    /// </summary>
    private static bool UseInlinePath(InterceptedMethodModel method, int loggableCount, InterceptedClassModel cls)
        => method.IsGenericMethod || cls.TypeParameters.Count > 0 || loggableCount > 6;

    // === EmitBefore: log entry ===

    public void EmitBefore(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        var logParams = P(aspect, "LogParameters", true, cls);
        var objectLogging = P(aspect, "ObjectLogging", 1, cls);
        var loggable = logParams ? method.Parameters.Where(p => !p.IsNoLog).ToList()
            : new List<InterceptedParameterModel>();

        sb.AppendLine($"{indent}var __sp = global::ZibStack.NET.Aop.AspectServiceProvider.ServiceProvider");
        sb.AppendLine($"{indent}    ?? throw new global::System.InvalidOperationException(");
        sb.AppendLine($"{indent}        \"ZibStack.NET.Aop.AspectServiceProvider.ServiceProvider is not set. \" +");
        sb.AppendLine($"{indent}        \"[Log] resolves ILogger<T> from DI; you must wire it once at app startup. \" +");
        sb.AppendLine($"{indent}        \"For ASP.NET Core: 'var app = builder.Build(); app.Services.UseAop();'\");");
        // For generic classes we can't cache ILogger<T> in a static field (different T
        // values must resolve to distinct ILogger<T> instances), so resolve inline.
        var loggerCategory = cls.TypeParameters.Count > 0
            ? $"{cls.ClassName}<{string.Join(", ", cls.TypeParameters.Select(t => t.Name))}>"
            : cls.ClassName;
        if (cls.TypeParameters.Count == 0)
        {
            sb.AppendLine($"{indent}var __logger = __cachedLogger ??= (global::Microsoft.Extensions.Logging.ILogger)__sp.GetService(typeof(global::Microsoft.Extensions.Logging.ILogger<{loggerCategory}>))!;");
        }
        else
        {
            sb.AppendLine($"{indent}var __logger = (global::Microsoft.Extensions.Logging.ILogger)__sp.GetService(typeof(global::Microsoft.Extensions.Logging.ILogger<{loggerCategory}>))!;");
        }

        if (!UseInlinePath(method, loggable.Count, cls))
        {
            var args = string.Join("", loggable.Select(p => FormatEntryArg(p, objectLogging)));
            sb.AppendLine($"{indent}__log{method.MethodName}Entry(__logger{args}, null);");
        }
        else
        {
            // Inline fallback: used for >6 params OR when method declares its own type
            // parameters (can't be referenced from a static cache delegate).
            var levelName = LogLevelNames[P(aspect, "EntryExitLevel", 2, cls)];
            var msg = PStr(aspect, "EntryMessage") ?? BuildEntryMessage(cls, method, loggable, objectLogging);
            var argList = loggable.Count == 0
                ? ""
                : ", " + string.Join(", ", loggable.Select(p => p.IsSensitive ? "(object)\"***\"" : $"(object){p.Name}"));
            sb.AppendLine($"{indent}if (__logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.{levelName}))");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    __logger.Log(global::Microsoft.Extensions.Logging.LogLevel.{levelName}, \"{Esc(msg)}\"{argList});");
            sb.AppendLine($"{indent}}}");
        }
    }

    // === EmitAfter: log exit ===

    public void EmitAfter(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        var measureElapsed = P(aspect, "MeasureElapsed", true, cls);
        var logReturn = P(aspect, "LogReturnValue", true, cls) && !aspect.NoLogReturn;
        var objectLogging = P(aspect, "ObjectLogging", 1, cls);

        // Compute loggable-count so UseInlinePath matches EmitClassMembers' decision.
        var logParams = P(aspect, "LogParameters", true, cls);
        var loggableCount = logParams ? method.Parameters.Count(p => !p.IsNoLog) : 0;

        if (UseInlinePath(method, loggableCount, cls))
        {
            var levelName = LogLevelNames[P(aspect, "EntryExitLevel", 2, cls)];
            var msg = PStr(aspect, "ExitMessage") ?? BuildExitMessage(cls, method, measureElapsed, logReturn, objectLogging);
            var args = new List<string>();
            if (measureElapsed) args.Add("(object)__sw.ElapsedMilliseconds");
            if (logReturn && !method.ReturnsVoid) args.Add($"(object){FormatReturnValue(method, aspect, objectLogging)}");
            var argList = args.Count == 0 ? "" : ", " + string.Join(", ", args);
            sb.AppendLine($"{indent}if (__logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.{levelName}))");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    __logger.Log(global::Microsoft.Extensions.Logging.LogLevel.{levelName}, \"{Esc(msg)}\"{argList});");
            sb.AppendLine($"{indent}}}");
            return;
        }

        if (method.ReturnsVoid)
        {
            if (measureElapsed)
                sb.AppendLine($"{indent}__log{method.MethodName}Exit(__logger, __sw.ElapsedMilliseconds, null);");
            else
                sb.AppendLine($"{indent}__log{method.MethodName}Exit(__logger, null);");
        }
        else
        {
            var args = new List<string>();
            if (measureElapsed) args.Add("__sw.ElapsedMilliseconds");
            if (logReturn) args.Add(FormatReturnValue(method, aspect, objectLogging));

            if (args.Count > 0)
                sb.AppendLine($"{indent}__log{method.MethodName}Exit(__logger, {string.Join(", ", args)}, null);");
            else
                sb.AppendLine($"{indent}__log{method.MethodName}Exit(__logger, null);");
        }
    }

    // === EmitOnException: log error ===

    public void EmitOnException(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        var measureElapsed = P(aspect, "MeasureElapsed", true, cls);
        var logParams = P(aspect, "LogParameters", true, cls);
        var loggableCount = logParams ? method.Parameters.Count(p => !p.IsNoLog) : 0;

        if (UseInlinePath(method, loggableCount, cls))
        {
            var levelName = LogLevelNames[P(aspect, "ExceptionLevel", 4, cls)];
            var msg = PStr(aspect, "ExceptionMessage") ?? BuildErrorMessage(cls, method, measureElapsed);
            var argList = measureElapsed ? ", (object)__sw.ElapsedMilliseconds" : "";
            sb.AppendLine($"{indent}__logger.Log(global::Microsoft.Extensions.Logging.LogLevel.{levelName}, __ex, \"{Esc(msg)}\"{argList});");
            return;
        }

        if (measureElapsed)
            sb.AppendLine($"{indent}__log{method.MethodName}Error(__logger, __sw.ElapsedMilliseconds, __ex);");
        else
            sb.AppendLine($"{indent}__log{method.MethodName}Error(__logger, __ex);");
    }

    // === Format helpers ===

    private static string GetParamLogType(InterceptedParameterModel p, int objectLogging)
    {
        if (p.IsSensitive) return "string";
        if (p.SanitizedType != null)
            return objectLogging == 1 ? "object" : "string"; // Destructure: dict as object, else string
        if (objectLogging == 2 && p.IsComplexType) return "string";
        return p.FullyQualifiedType;
    }

    private static string FormatEntryArg(InterceptedParameterModel p, int objectLogging)
    {
        if (p.IsSensitive) return ", \"***\"";
        if (p.SanitizedType != null)
        {
            if (objectLogging == 1) return $", __SanitizeDict_{p.SanitizedType.SafeName}({p.Name})";
            return $", __Sanitize_{p.SanitizedType.SafeName}({p.Name})";
        }
        if (objectLogging == 2 && p.IsComplexType)
            return $", global::System.Text.Json.JsonSerializer.Serialize({p.Name})";
        return $", {p.Name}";
    }

    private static string FormatReturnValue(InterceptedMethodModel method, AspectInfo aspect, int objectLogging)
    {
        if (aspect.SensitiveReturn) return "\"***\"";
        if (method.SanitizedReturnType != null)
        {
            if (objectLogging == 1) return $"__SanitizeDict_{method.SanitizedReturnType.SafeName}(__result)";
            return $"__Sanitize_{method.SanitizedReturnType.SafeName}(__result)";
        }
        if (objectLogging == 2 && method.HasComplexReturnType)
            return "global::System.Text.Json.JsonSerializer.Serialize(__result)";
        return "((object?)__result)?.ToString()";
    }

    private static string BuildEntryMessage(InterceptedClassModel cls, InterceptedMethodModel method,
        List<InterceptedParameterModel> loggable, int objectLogging)
    {
        var sb = new StringBuilder();
        sb.Append($"Entering {cls.ClassName}.{method.MethodName}(");
        var parts = new List<string>();
        foreach (var p in loggable)
        {
            if (p.IsSensitive)
                parts.Add($"{p.Name}: {{__sensitive_{p.Name}}}");
            else if (objectLogging == 1 && p.IsComplexType) // Destructure
                parts.Add($"{p.Name}: {{@{p.Name}}}");
            else
                parts.Add($"{p.Name}: {{{p.Name}}}");
        }
        sb.Append(string.Join(", ", parts));
        sb.Append(')');
        return sb.ToString();
    }

    private static string BuildExitMessage(InterceptedClassModel cls, InterceptedMethodModel method,
        bool measureElapsed, bool logReturn, int objectLogging)
    {
        var sb = new StringBuilder();
        sb.Append($"Exited {cls.ClassName}.{method.MethodName}");
        var parts = new List<string>();
        if (measureElapsed) parts.Add("in {ElapsedMs}ms");
        if (logReturn && !method.ReturnsVoid)
        {
            if (objectLogging == 1) // Destructure
                parts.Add("-> {@Result}");
            else
                parts.Add("-> {Result}");
        }
        if (parts.Count > 0) { sb.Append(" "); sb.Append(string.Join(" ", parts)); }
        return sb.ToString();
    }

    private static string BuildErrorMessage(InterceptedClassModel cls, InterceptedMethodModel method, bool measureElapsed)
    {
        return measureElapsed
            ? $"{cls.ClassName}.{method.MethodName} failed after {{ElapsedMs}}ms"
            : $"{cls.ClassName}.{method.MethodName} failed";
    }

    // === Shared emit helper ===

    private static void EmitDelegate(StringBuilder sb, string indent, string methodName,
        string suffix, List<string> typeArgs, int level, ref int eventId, string message)
    {
        var typeArgsStr = typeArgs.Count > 0 ? $"<{string.Join(", ", typeArgs)}>" : "";
        var all = new List<string> { "global::Microsoft.Extensions.Logging.ILogger" };
        all.AddRange(typeArgs);
        all.Add("global::System.Exception?");

        sb.AppendLine($"{indent}private static readonly global::System.Action<{string.Join(", ", all)}> __log{methodName}{suffix} =");
        sb.AppendLine($"{indent}    global::Microsoft.Extensions.Logging.LoggerMessage.Define{typeArgsStr}(");
        sb.AppendLine($"{indent}        global::Microsoft.Extensions.Logging.LogLevel.{LogLevelNames[level]},");
        sb.AppendLine($"{indent}        new global::Microsoft.Extensions.Logging.EventId({eventId++}, \"{methodName}_{suffix}\"),");
        sb.AppendLine($"{indent}        \"{Esc(message)}\");");
    }

    private static object? ClassData(InterceptedClassModel cls, string key)
    {
        if (cls.AspectClassData.TryGetValue("ZibStack.NET.Log.LogAttribute", out var d) && d.TryGetValue(key, out var v)) return v;
        return null;
    }

    /// <summary>Get int property: per-method override > assembly default > hardcoded default.</summary>
    private static int P(AspectInfo a, string k, int def, InterceptedClassModel? cls = null)
    {
        if (a.Properties.TryGetValue(k, out var v) && v is int i) return i;
        if (cls != null && ClassData(cls, $"Default_{k}") is int d && d != -1) return d;
        return def;
    }

    /// <summary>Get bool property: per-method override > assembly default > hardcoded default.</summary>
    private static bool P(AspectInfo a, string k, bool def, InterceptedClassModel? cls = null)
    {
        if (a.Properties.TryGetValue(k, out var v) && v is bool b) return b;
        if (cls != null && ClassData(cls, $"Default_{k}") is bool d) return d;
        return def;
    }

    private static string? PStr(AspectInfo a, string k) => a.Properties.TryGetValue(k, out var v) && v is string s ? s : null;
    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // === Sanitizer emission (property-level [Sensitive]/[NoLog]) ===

    private static void EmitSanitizer(StringBuilder sb, SanitizedTypeModel model, HashSet<string> emitted, string indent)
    {
        if (!emitted.Add(model.SafeName)) return;

        foreach (var prop in model.Properties)
        {
            if (prop.NestedProperties != null)
            {
                var nested = new SanitizedTypeModel(prop.FullyQualifiedType,
                    prop.FullyQualifiedType.Replace("global::", "").Replace(".", "_")
                        .Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", ""),
                    prop.NestedProperties);
                EmitSanitizer(sb, nested, emitted, indent);
            }
        }

        // Dict version (for Destructure mode — Serilog destructures Dictionary as object)
        sb.AppendLine($"{indent}private static global::System.Collections.Generic.Dictionary<string, object?> __SanitizeDict_{model.SafeName}({model.FullyQualifiedType} __obj)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    var __dict = new global::System.Collections.Generic.Dictionary<string, object?>();");
        EmitDictEntries(sb, model, indent + "    ");
        sb.AppendLine($"{indent}    return __dict;");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();

        // String version (for JSON/ToString mode)
        sb.AppendLine($"{indent}private static string __Sanitize_{model.SafeName}({model.FullyQualifiedType} __obj)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if (__obj == null) return \"null\";");
        sb.AppendLine($"{indent}    return global::System.Text.Json.JsonSerializer.Serialize(__SanitizeDict_{model.SafeName}(__obj));");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    private static void EmitDictEntries(StringBuilder sb, SanitizedTypeModel model, string indent)
    {
        foreach (var prop in model.Properties)
        {
            if (prop.IsNoLog) continue;
            if (prop.IsSensitive)
            {
                sb.AppendLine($"{indent}__dict[\"{prop.Name}\"] = \"***\";");
            }
            else if (prop.NestedProperties != null)
            {
                var nestedSafe = prop.FullyQualifiedType.Replace("global::", "").Replace(".", "_")
                    .Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", "");
                sb.AppendLine($"{indent}__dict[\"{prop.Name}\"] = __obj.{prop.Name} != null ? __SanitizeDict_{nestedSafe}(__obj.{prop.Name}) : null;");
            }
            else
            {
                sb.AppendLine($"{indent}__dict[\"{prop.Name}\"] = __obj.{prop.Name};");
            }
        }
    }
}

/// <summary>
/// Reads assembly-level [ZibLogDefaults] and reports diagnostics for [Log] methods.
/// Logger is resolved from DI — no [ZibLog] or field detection needed.
/// </summary>
internal sealed class LogClassDataProvider : IClassDataProvider
{
    public string AttributeFullName => "ZibStack.NET.Log.LogAttribute";

    public IReadOnlyDictionary<string, object?>? ExtractClassData(INamedTypeSymbol classSymbol)
    {
        // Check if class has any [Log] methods
        bool hasLog = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Any(m => m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Log.LogAttribute"));
        if (!hasLog) return null;

        var data = new Dictionary<string, object?>();

        // Read assembly-level [ZibLogDefaults]
        foreach (var asmAttr in classSymbol.ContainingAssembly.GetAttributes())
        {
            if (asmAttr.AttributeClass?.ToDisplayString() == "ZibStack.NET.Log.ZibLogDefaultsAttribute")
            {
                foreach (var arg in asmAttr.NamedArguments)
                    data[$"Default_{arg.Key}"] = arg.Value.Value;
            }
        }

        return data.Count > 0 ? data : null;
    }

    public IEnumerable<Diagnostic> GetDiagnostics(INamedTypeSymbol classSymbol)
    {
        // Check for static methods with [Log]
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.IsStatic &&
                method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Log.LogAttribute"))
            {
                var loc = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation()
                    ?? classSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
                if (loc != null)
                    yield return Diagnostic.Create(DiagnosticDescriptors.StaticMethodNotSupported, loc, method.Name);
            }
        }
    }
}
