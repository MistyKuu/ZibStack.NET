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
        title: "[Cache] on a void/Task method silently suppresses subsequent calls",
        messageFormat: "[Cache] on '{0}' (returns void or non-generic Task) suppresses every call after the first — including any side effects in the body. If the side effects must fire each call, remove [Cache]. If you meant 'memoize this call once', this is the right shape.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Verified by behavioral test: [Cache] applied to a void method runs the body once, then short-circuits subsequent calls without re-executing. The original 'has no effect' wording was wrong — the cache DOES intercept, it just has no return value to store. The actual hazard is silent side-effect suppression.");

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

    // AOP0015 was here — TimeoutNoCancellationToken. Removed because the
    // TimeoutHandler doesn't actually use any CancellationToken: it does pure
    // Task.WhenAny(work, Task.Delay(timeoutMs)). Adding a CT param to satisfy
    // the analyzer wouldn't change behavior — the handler would still ignore it
    // and the body would still leak. The right fix is in the handler (create a
    // CTS, signal it on timeout, propagate to a CT param if present), not in a
    // misleading per-method warning.

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

    // ── Tier 3: Call-site analysis (AOP0020-AOP0021) ────────────────────────

    public const string DelegateConversionId = "AOP0020";
    public static readonly DiagnosticDescriptor DelegateConversion = new(
        DelegateConversionId,
        title: "Method group conversion bypasses aspects",
        messageFormat: "Aspect on '{0}' will be skipped here — converting to a delegate captures the original method directly. Calls through the delegate will not be intercepted.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string BaseCallId = "AOP0021";
    public static readonly DiagnosticDescriptor BaseCall = new(
        BaseCallId,
        title: "base.Method() to an aspect-decorated virtual method causes infinite recursion",
        messageFormat: "base.{0}() recurses infinitely at runtime — the interceptor IS bound to this call site and dispatches `@this.{0}(...)` virtually back to the override, which calls base again. Either remove the override, remove the aspect, or reshape the call to avoid `base.`.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generated interceptors invoke the target through `@this.Method(args)` to preserve generic-virtual-call semantics. For a `base.Method()` call from inside an override, that re-dispatches virtually back to the override, producing guaranteed StackOverflowException at runtime. Verified by behavioral experiment in AopGeneratorTests.");

    // ── Convention enforcement (AOP1001-AOP1099) ────────────────────────────

    public const string MissingRequiredAspectId = "AOP1001";
    public static readonly DiagnosticDescriptor MissingRequiredAspect = new(
        MissingRequiredAspectId,
        title: "Type is missing an aspect required by its base/interface",
        messageFormat: "'{0}' derives from '{1}' which requires [{2}]{3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A base type or interface declared with [RequireAspect(typeof(X))] expects every concrete derivative to also carry [X]. Without it, the configured runtime behavior won't apply and the derivative falls into a silent edge case.");

    public const string MissingRequiredImplementationId = "AOP1002";
    public static readonly DiagnosticDescriptor MissingRequiredImplementation = new(
        MissingRequiredImplementationId,
        title: "Type is missing an interface required by its base/interface",
        messageFormat: "'{0}' derives from '{1}' which requires implementing '{2}'{3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A base type or interface declared with [RequireImplementation(typeof(I))] expects every concrete derivative to also implement I. Use this for cross-cutting capabilities (IDisposable, IAsyncDisposable, custom marker interfaces) that the base type cannot inherit directly.");

    public const string MissingRequiredMethodId = "AOP1003";
    public static readonly DiagnosticDescriptor MissingRequiredMethod = new(
        MissingRequiredMethodId,
        title: "Type is missing a method required by its base/interface",
        messageFormat: "'{0}' derives from '{1}' which requires a method '{2}'{3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A base type or interface declared with [RequireMethod(\"X\")] expects every concrete derivative to declare a method named X (and matching the optional return type / parameter list). Use this for plug-in conventions called by reflection where the framework cannot enforce the contract through inheritance.");

    public const string MissingRequiredConstructorId = "AOP1004";
    public static readonly DiagnosticDescriptor MissingRequiredConstructor = new(
        MissingRequiredConstructorId,
        title: "Type is missing a constructor required by its base/interface",
        messageFormat: "'{0}' derives from '{1}' which requires a public constructor '{2}'{3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A base type or interface declared with [RequireConstructor(typeof(...))] expects every concrete derivative to expose a public constructor with the configured parameter list. Use this for DI-activated bases where a missing matching ctor turns into a runtime resolution exception instead of a compile-time error.");

    public const string OutOfScopeUsageId = "AOP1005";
    public static readonly DiagnosticDescriptor OutOfScopeUsage = new(
        OutOfScopeUsageId,
        title: "Type is used outside its allowed scope",
        messageFormat: "'{0}' may only be used from {1} (call-site namespace: '{2}'){3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A type declared with [ScopeTo(\"NS\")] restricts which namespaces may reference it. Use this to carve out a private API surface inside a single library or to gate test-only helpers behind an explicit scope rule.");
}
