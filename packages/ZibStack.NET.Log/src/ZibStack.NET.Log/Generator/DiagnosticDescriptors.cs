using Microsoft.CodeAnalysis;

namespace ZibStack.NET.Log.Generator;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor LogWithoutZibLog = new(
        id: "SL0001",
        title: "Method has [Log] but class lacks [ZibLog]",
        messageFormat: "Method '{0}' has [Log] attribute but its containing class '{1}' does not have [ZibLog] attribute",
        category: "ZibStack.NET.Log",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoLoggerField = new(
        id: "SL0002",
        title: "No ILogger field found",
        messageFormat: "No ILogger or ILogger<T> field found in class '{0}'. Add an ILogger field or set LoggerField.",
        category: "ZibStack.NET.Log",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleLoggerFields = new(
        id: "SL0003",
        title: "Multiple ILogger fields found",
        messageFormat: "Multiple ILogger fields found in class '{0}': {1}. Set LoggerField to choose one.",
        category: "ZibStack.NET.Log",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StaticMethodNotSupported = new(
        id: "SL0005",
        title: "Static methods are not supported",
        messageFormat: "Method '{0}' is static. The [Log] attribute only supports instance methods.",
        category: "ZibStack.NET.Log",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SpecifiedLoggerFieldNotFound = new(
        id: "SL0006",
        title: "Specified logger field not found",
        messageFormat: "Logger field '{0}' specified in [ZibLog(LoggerField = \"{0}\")] was not found in class '{1}'",
        category: "ZibStack.NET.Log",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
