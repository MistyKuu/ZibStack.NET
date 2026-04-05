using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Static service locator for resolving aspect handlers from DI.
/// Set <see cref="ServiceProvider"/> at startup to enable DI for all aspect handlers.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddTransient&lt;TimingHandler&gt;();
/// var app = builder.Build();
/// AspectServiceProvider.ServiceProvider = app.Services;
/// </code>
/// </example>
public static class AspectServiceProvider
{
    /// <summary>
    /// The application's service provider. Must be set at startup.
    /// </summary>
    public static IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// Resolves a handler from DI. Throws if DI is not configured or the handler is not registered.
    /// </summary>
    public static T Resolve<T>() where T : class
    {
        if (ServiceProvider is null)
            throw new InvalidOperationException(
                $"AspectServiceProvider.ServiceProvider is not set. " +
                $"Call 'AspectServiceProvider.ServiceProvider = app.Services;' at startup.");

        var service = ServiceProvider.GetService(typeof(T)) as T;
        if (service is null)
            throw new InvalidOperationException(
                $"Aspect handler '{typeof(T).FullName}' is not registered in DI. " +
                $"Add 'builder.Services.AddTransient<{typeof(T).Name}>();' at startup.");

        return service;
    }
}
