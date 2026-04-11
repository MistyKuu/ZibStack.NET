using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Extension methods that make wiring the aspect runtime a one-liner.
/// Prefer these over setting <see cref="AspectServiceProvider.ServiceProvider"/> by hand.
/// </summary>
/// <example>
/// <code>
/// var app = builder.Build();
/// app.UseAop();  // bridges DI into the aspect runtime
/// </code>
/// </example>
public static class AspectServiceProviderExtensions
{
    /// <summary>
    /// Bridges an <see cref="IServiceProvider"/> into the static
    /// <see cref="AspectServiceProvider"/> used by generated aspect interceptors.
    /// Call once at startup (e.g. right after <c>WebApplication.Build()</c>).
    /// </summary>
    /// <param name="services">The application's service provider.</param>
    /// <returns>The same service provider, for chaining.</returns>
    public static IServiceProvider UseAop(this IServiceProvider services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        AspectServiceProvider.ServiceProvider = services;
        return services;
    }
}
