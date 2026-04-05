using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using ZibStack.NET.Aop.Generator;

namespace ZibStack.NET.Log.Generator;

/// <summary>
/// Compile-time inline emitter for the [Log] aspect.
/// Generates LoggerMessage.Define delegates, UnsafeAccessor, and structured logging code.
/// </summary>
internal sealed class LogAspectEmitter : IAspectEmitter
{
    public string AttributeFullName => "ZibStack.NET.Log.LogAttribute";

    private static readonly string[] LogLevelNames =
        { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" };

    private int _eventId = 1;
    private readonly HashSet<string> _emittedAccessors = new();

    public IEnumerable<string> RequiredUsings => new[] { "Microsoft.Extensions.Logging" };

    public void EmitClassMembers(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        var loggerFieldName = GetClassData(cls, "LoggerFieldName") as string ?? "_logger";
        var loggerFieldType = GetClassData(cls, "LoggerFieldType") as string
            ?? $"global::Microsoft.Extensions.Logging.ILogger<{cls.ClassName}>";

        // UnsafeAccessor (once per class)
        if (_emittedAccessors.Add(cls.ClassName))
        {
            sb.AppendLine($"{indent}[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"{loggerFieldName}\")]");
            sb.AppendLine($"{indent}private static extern ref {loggerFieldType} __GetLogger({cls.ClassName} @this);");
            sb.AppendLine();
        }

        var entryExitLevel = Prop(aspect, "EntryExitLevel", 2);
        var exceptionLevel = Prop(aspect, "ExceptionLevel", 4);
        var logParams = Prop(aspect, "LogParameters", true);
        var measureElapsed = Prop(aspect, "MeasureElapsed", true);
        var logReturn = Prop(aspect, "LogReturnValue", true) && !aspect.NoLogReturn;

        var loggable = logParams
            ? method.Parameters.Where(p => !p.IsNoLog).ToList()
            : new List<InterceptedParameterModel>();

        // --- Entry delegate ---
        var entryMsg = PropStr(aspect, "EntryMessage")
            ?? BuildMsg($"Entering {cls.ClassName}.{method.MethodName}(",
                loggable.Select(p => p.IsSensitive ? $"{p.Name}: {{__s_{p.Name}}}" : $"{p.Name}: {{{p.Name}}}"), ")");

        if (loggable.Count <= 6)
        {
            var types = loggable.Select(p => p.IsSensitive ? "string" : p.FullyQualifiedType).ToList();
            EmitDefineDelegate(sb, indent, method.MethodName, "Entry", types, entryExitLevel, ref _eventId, entryMsg);
        }
        else _eventId++;

        // --- Exit delegate ---
        var exitTypes = new List<string>();
        if (measureElapsed) exitTypes.Add("long");
        if (logReturn && !method.ReturnsVoid) exitTypes.Add("string?");

        var exitMsg = PropStr(aspect, "ExitMessage") ?? BuildExitMsg(cls, method, measureElapsed, logReturn);
        EmitDefineDelegate(sb, indent, method.MethodName, "Exit", exitTypes, entryExitLevel, ref _eventId, exitMsg);

        // --- Error delegate ---
        var errTypes = measureElapsed ? new List<string> { "long" } : new List<string>();
        var errMsg = PropStr(aspect, "ExceptionMessage")
            ?? (measureElapsed ? $"{cls.ClassName}.{method.MethodName} failed after {{ElapsedMs}}ms"
                              : $"{cls.ClassName}.{method.MethodName} failed");
        EmitDefineDelegate(sb, indent, method.MethodName, "Error", errTypes, exceptionLevel, ref _eventId, errMsg);
    }

    public void EmitBefore(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        var logParams = Prop(aspect, "LogParameters", true);
        var loggable = logParams
            ? method.Parameters.Where(p => !p.IsNoLog).ToList()
            : new List<InterceptedParameterModel>();

        sb.AppendLine($"{indent}var __logger = __GetLogger(@this);");
        if (loggable.Count <= 6)
        {
            var args = string.Join("", loggable.Select(p => p.IsSensitive ? ", \"***\"" : $", {p.Name}"));
            sb.AppendLine($"{indent}__log{method.MethodName}Entry(__logger{args}, null);");
        }
    }

    public void EmitAfter(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        var measureElapsed = Prop(aspect, "MeasureElapsed", true);
        var logReturn = Prop(aspect, "LogReturnValue", true) && !aspect.NoLogReturn;

        var args = new List<string>();
        if (measureElapsed) args.Add("__sw.ElapsedMilliseconds");
        if (logReturn && !method.ReturnsVoid)
            args.Add(aspect.SensitiveReturn ? "\"***\"" : "((object?)__result)?.ToString()");

        if (args.Count > 0)
            sb.AppendLine($"{indent}__log{method.MethodName}Exit(__logger, {string.Join(", ", args)}, null);");
        else
            sb.AppendLine($"{indent}__log{method.MethodName}Exit(__logger, null);");
    }

    public void EmitOnException(StringBuilder sb, InterceptedClassModel cls,
        InterceptedMethodModel method, AspectInfo aspect, string indent)
    {
        if (Prop(aspect, "MeasureElapsed", true))
            sb.AppendLine($"{indent}__log{method.MethodName}Error(__logger, __sw.ElapsedMilliseconds, __ex);");
        else
            sb.AppendLine($"{indent}__log{method.MethodName}Error(__logger, __ex);");
    }

    // === Helpers ===

    private static void EmitDefineDelegate(StringBuilder sb, string indent, string methodName,
        string suffix, List<string> typeArgs, int level, ref int eventId, string message)
    {
        var typeArgsStr = typeArgs.Count > 0 ? $"<{string.Join(", ", typeArgs)}>" : "";
        var allTypes = new List<string> { "global::Microsoft.Extensions.Logging.ILogger" };
        allTypes.AddRange(typeArgs);
        allTypes.Add("global::System.Exception?");
        var delegateType = $"global::System.Action<{string.Join(", ", allTypes)}>";

        sb.AppendLine($"{indent}private static readonly {delegateType} __log{methodName}{suffix} =");
        sb.AppendLine($"{indent}    global::Microsoft.Extensions.Logging.LoggerMessage.Define{typeArgsStr}(");
        sb.AppendLine($"{indent}        global::Microsoft.Extensions.Logging.LogLevel.{LogLevelNames[level]},");
        sb.AppendLine($"{indent}        new global::Microsoft.Extensions.Logging.EventId({eventId++}, \"{methodName}_{suffix}\"),");
        sb.AppendLine($"{indent}        \"{Esc(message)}\");");
    }

    private static object? GetClassData(InterceptedClassModel cls, string key)
    {
        if (cls.AspectClassData.TryGetValue("ZibStack.NET.Log.LogAttribute", out var d) && d.TryGetValue(key, out var v)) return v;
        return null;
    }

    private static int Prop(AspectInfo a, string k, int def) => a.Properties.TryGetValue(k, out var v) && v is int i ? i : def;
    private static bool Prop(AspectInfo a, string k, bool def) => a.Properties.TryGetValue(k, out var v) && v is bool b ? b : def;
    private static string? PropStr(AspectInfo a, string k) => a.Properties.TryGetValue(k, out var v) && v is string s ? s : null;

    private static string BuildMsg(string prefix, IEnumerable<string> parts, string suffix)
        => prefix + string.Join(", ", parts) + suffix;

    private static string BuildExitMsg(InterceptedClassModel cls, InterceptedMethodModel m, bool elapsed, bool ret)
    {
        var parts = new List<string>();
        if (elapsed) parts.Add("in {ElapsedMs}ms");
        if (ret && !m.ReturnsVoid) parts.Add("-> {Result}");
        return $"Exited {cls.ClassName}.{m.MethodName}" + (parts.Count > 0 ? " " + string.Join(" ", parts) : "");
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>
/// Extracts logger field info from [ZibLog]-annotated classes.
/// </summary>
internal sealed class LogClassDataProvider : IClassDataProvider
{
    private const string ZibLogAttr = "ZibStack.NET.Log.ZibLogAttribute";
    private const string ILoggerName = "Microsoft.Extensions.Logging.ILogger";
    private const string ILoggerGenericName = "Microsoft.Extensions.Logging.ILogger`1";

    public string AttributeFullName => "ZibStack.NET.Log.LogAttribute";

    public IReadOnlyDictionary<string, object?>? ExtractClassData(INamedTypeSymbol classSymbol)
    {
        // Check if class has [ZibLog]
        string? loggerFieldOverride = null;
        bool hasZibLog = false;
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ZibLogAttr)
            {
                hasZibLog = true;
                foreach (var arg in attr.NamedArguments)
                    if (arg.Key == "LoggerField" && arg.Value.Value is string f)
                        loggerFieldOverride = f;
            }
        }
        if (!hasZibLog) return null;

        // Find logger field
        var loggerFields = new List<(string name, string type)>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IFieldSymbol field && IsILogger(field.Type))
                loggerFields.Add((field.Name, field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        string? fieldName, fieldType;
        if (loggerFieldOverride != null)
        {
            var match = loggerFields.FirstOrDefault(f => f.name == loggerFieldOverride);
            fieldName = match.name;
            fieldType = match.type;
        }
        else if (loggerFields.Count == 1)
        {
            fieldName = loggerFields[0].name;
            fieldType = loggerFields[0].type;
        }
        else
        {
            fieldName = null;
            fieldType = null;
        }

        if (fieldName == null) return null;

        return new Dictionary<string, object?>
        {
            ["LoggerFieldName"] = fieldName,
            ["LoggerFieldType"] = fieldType
        };
    }

    public IEnumerable<Diagnostic> GetDiagnostics(INamedTypeSymbol classSymbol)
    {
        bool hasZibLog = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == ZibLogAttr);
        if (!hasZibLog) yield break;

        var loggerFields = new List<string>();
        foreach (var member in classSymbol.GetMembers())
            if (member is IFieldSymbol field && IsILogger(field.Type))
                loggerFields.Add(field.Name);

        string? loggerFieldOverride = null;
        foreach (var attr in classSymbol.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == ZibLogAttr)
                foreach (var arg in attr.NamedArguments)
                    if (arg.Key == "LoggerField" && arg.Value.Value is string f)
                        loggerFieldOverride = f;

        if (loggerFields.Count == 0)
        {
            var loc = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
            if (loc != null)
                yield return Diagnostic.Create(DiagnosticDescriptors.NoLoggerField, loc, classSymbol.Name);
        }
        else if (loggerFields.Count > 1 && loggerFieldOverride == null)
        {
            var loc = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
            if (loc != null)
                yield return Diagnostic.Create(DiagnosticDescriptors.MultipleLoggerFields, loc,
                    classSymbol.Name, string.Join(", ", loggerFields));
        }

        // Check specified logger field exists
        if (loggerFieldOverride != null && !loggerFields.Contains(loggerFieldOverride))
        {
            var loc = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
            if (loc != null)
                yield return Diagnostic.Create(DiagnosticDescriptors.SpecifiedLoggerFieldNotFound, loc,
                    loggerFieldOverride, classSymbol.Name);
        }

        // Check for static methods with [Log]
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.IsStatic)
            {
                bool hasLog = method.GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Log.LogAttribute");
                if (hasLog)
                {
                    var loc = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation()
                        ?? classSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
                    if (loc != null)
                        yield return Diagnostic.Create(DiagnosticDescriptors.StaticMethodNotSupported, loc, method.Name);
                }
            }
        }
    }

    private static bool IsILogger(ITypeSymbol type)
    {
        if (type.ToDisplayString() == ILoggerName) return true;
        if (type is INamedTypeSymbol nt && nt.IsGenericType)
        {
            var meta = nt.OriginalDefinition.ContainingNamespace + "." + nt.OriginalDefinition.MetadataName;
            if (meta == ILoggerGenericName) return true;
        }
        return false;
    }
}
