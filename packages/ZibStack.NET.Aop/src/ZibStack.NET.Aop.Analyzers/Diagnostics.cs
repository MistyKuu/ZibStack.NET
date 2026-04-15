using Microsoft.CodeAnalysis;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Central registry of diagnostic IDs and descriptors emitted by the AOP analyzers.
/// Grouping them here keeps IDs unique, makes editorconfig suppressions discoverable,
/// and avoids duplicating message text across analyzer classes.
/// </summary>
public static class Diagnostics
{
    private const string Category = "ZibStack.Aop";

    // ── Tier 1: Universal placement checks (AOP0001-AOP0006) ────────────────

    public const string StaticMethodId = "AOP0001";
    public static readonly DiagnosticDescriptor StaticMethod = new(
        StaticMethodId,
        title: "Aspect cannot intercept static methods",
        messageFormat: "Aspect '{0}' on static method '{1}' has no effect — C# interceptors require an instance receiver.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ZibStack.NET.Aop generates extension-method interceptors that take a `this` receiver. Static methods cannot be intercepted; remove the aspect or convert the method to an instance method.");

    public const string PrivateOrProtectedId = "AOP0002";
    public static readonly DiagnosticDescriptor PrivateOrProtected = new(
        PrivateOrProtectedId,
        title: "Aspect cannot intercept private/protected methods",
        messageFormat: "Aspect '{0}' on '{1}' has no effect — interceptors live in a separate `__X_Aop` class and cannot access private/protected members. Make the method internal or public.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated interceptor is not a member of the target class nor a derived class, so private/protected/private-protected access rules forbid it from invoking the original method.");

    public const string RefOutInParamId = "AOP0003";
    public static readonly DiagnosticDescriptor RefOutInParam = new(
        RefOutInParamId,
        title: "Aspect cannot intercept methods with ref/out/in parameters",
        messageFormat: "Aspect '{0}' on '{1}' has no effect — ref/out/in parameters cannot flow through the generated interceptor's `params object[]` context capture.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string RefReturnId = "AOP0003B";
    public static readonly DiagnosticDescriptor RefReturn = new(
        RefReturnId,
        title: "Aspect cannot intercept methods returning by ref",
        messageFormat: "Aspect '{0}' on '{1}' has no effect — ref / ref readonly returns cannot be aliased through the interceptor wrapper.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string HandlerTypeMismatchId = "AOP0004";
    public static readonly DiagnosticDescriptor HandlerTypeMismatch = new(
        HandlerTypeMismatchId,
        title: "AspectHandler type does not implement a handler interface",
        messageFormat: "Type '{0}' referenced by [AspectHandler] does not implement IAspectHandler, IAsyncAspectHandler, IAroundAspectHandler, or IAsyncAroundAspectHandler.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string MissingHandlerAttributeId = "AOP0005";
    public static readonly DiagnosticDescriptor MissingHandlerAttribute = new(
        MissingHandlerAttributeId,
        title: "Aspect attribute missing [AspectHandler]",
        messageFormat: "Aspect '{0}' has no [AspectHandler(typeof(...))] — the generator cannot resolve a handler for it and will skip emission.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string OperatorMethodId = "AOP0006";
    public static readonly DiagnosticDescriptor OperatorMethod = new(
        OperatorMethodId,
        title: "Aspect cannot intercept operators or conversions",
        messageFormat: "Aspect '{0}' on operator/conversion '{1}' has no effect — the generator only intercepts ordinary instance methods.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
