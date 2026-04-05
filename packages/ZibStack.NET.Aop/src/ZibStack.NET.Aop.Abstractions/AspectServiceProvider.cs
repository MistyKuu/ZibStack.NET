using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Static service locator for resolving aspect handlers from DI.
/// When configured, handlers are resolved from <see cref="ServiceProvider"/> instead of
/// being instantiated with <c>new</c>. This allows handlers to receive constructor-injected
/// dependencies (ILogger, IMemoryCache, IHttpContextAccessor, etc.).
/// </summary>
/// <example>
/// <code>
/// // Program.cs — register your handlers and wire up the provider:
/// builder.Services.AddTransient&lt;TimingHandler&gt;();
/// var app = builder.Build();
/// AspectServiceProvider.ServiceProvider = app.Services;
/// </code>
/// </example>
public static class AspectServiceProvider
{
    /// <summary>
    /// The application's service provider. Set this at startup to enable DI for aspect handlers.
    /// When null (default), handlers are created with parameterless constructors (backward compatible).
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// Resolves a handler from <see cref="ServiceProvider"/>, or returns null if DI is not configured
    /// or the type is not registered.
    /// </summary>
    public static T? Resolve<T>() where T : class
    {
        return ServiceProvider?.GetService(typeof(T)) as T;
    }
}
