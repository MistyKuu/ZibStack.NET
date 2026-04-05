using Microsoft.CodeAnalysis;

namespace ZibStack.NET.Log.Generator;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor StaticMethodNotSupported = new(
        id: "SL0005",
        title: "Static methods are not supported",
        messageFormat: "Method '{0}' is static. The [Log] attribute only supports instance methods.",
        category: "ZibStack.NET.Log",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
