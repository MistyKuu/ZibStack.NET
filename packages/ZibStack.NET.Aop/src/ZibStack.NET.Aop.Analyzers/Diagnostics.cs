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

    // ── Tier 2: Per-aspect semantic checks (AOP0010-AOP0017) ────────────────

    public const string CacheNonReturningId = "AOP0010";
    public static readonly DiagnosticDescriptor CacheNonReturning = new(
        CacheNonReturningId,
        title: "[Cache] on a method that returns nothing",
        messageFormat: "[Cache] on '{0}' has no effect — the method returns void or non-generic Task. Cache only methods that produce a value.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string RetryMaxAttemptsId = "AOP0011";
    public static readonly DiagnosticDescriptor RetryMaxAttempts = new(
        RetryMaxAttemptsId,
        title: "[Retry] MaxAttempts must be positive",
        messageFormat: "[Retry(MaxAttempts = {0})] is invalid — MaxAttempts must be at least 1 (it includes the first call).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string RetryDelayId = "AOP0012";
    public static readonly DiagnosticDescriptor RetryDelay = new(
        RetryDelayId,
        title: "[Retry] DelayMs must be non-negative",
        messageFormat: "[Retry(DelayMs = {0})] is invalid — DelayMs cannot be negative.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string RetryBackoffId = "AOP0013";
    public static readonly DiagnosticDescriptor RetryBackoff = new(
        RetryBackoffId,
        title: "[Retry] BackoffMultiplier should be at least 1.0",
        messageFormat: "[Retry(BackoffMultiplier = {0})] shrinks the delay between retries. Use 1.0 for constant delay or >1.0 for exponential backoff.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string TimeoutValueId = "AOP0014";
    public static readonly DiagnosticDescriptor TimeoutValue = new(
        TimeoutValueId,
        title: "[Timeout] TimeoutMs must be positive",
        messageFormat: "[Timeout(TimeoutMs = {0})] is invalid — TimeoutMs must be greater than 0.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string TimeoutNoCancellationTokenId = "AOP0015";
    public static readonly DiagnosticDescriptor TimeoutNoCancellationToken = new(
        TimeoutNoCancellationTokenId,
        title: "[Timeout] on a method that cannot observe cancellation",
        messageFormat: "[Timeout] on '{0}' will fire the token but the method has no CancellationToken parameter to observe it. Add a CancellationToken parameter or remove [Timeout].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string ValidateNoParametersId = "AOP0016";
    public static readonly DiagnosticDescriptor ValidateNoParameters = new(
        ValidateNoParametersId,
        title: "[Validate] on a parameterless method has no effect",
        messageFormat: "[Validate] on '{0}' has no effect — there are no parameters to validate.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string ValidateNoAnnotationsId = "AOP0017";
    public static readonly DiagnosticDescriptor ValidateNoAnnotations = new(
        ValidateNoAnnotationsId,
        title: "[Validate] cannot find any DataAnnotations to enforce",
        messageFormat: "[Validate] on '{0}' will be a no-op — none of the parameters or their reachable property graph carry DataAnnotations attributes.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
