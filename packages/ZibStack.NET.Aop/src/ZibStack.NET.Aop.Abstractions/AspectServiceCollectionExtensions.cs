using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZibStack.NET.Aop;

/// <summary>
/// DI registration helpers for the ZibStack.NET AOP runtime and its built-in aspects.
/// Paired with <see cref="AspectServiceProviderExtensions.UseAop(System.IServiceProvider)"/>.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddAop();   // registers built-in handlers (Trace, ...)
/// var app = builder.Build();
/// app.Services.UseAop();        // bridges DI into the aspect runtime
/// </code>
/// </example>
public static class AspectServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in ZibStack.NET aspect handlers in DI:
    /// <list type="bullet">
    ///   <item><see cref="TraceHandler"/> — OpenTelemetry spans (<see cref="TraceAttribute"/>)</item>
    ///   <item><see cref="RetryHandler"/> — retry with backoff (<see cref="RetryAttribute"/>)</item>
    ///   <item><see cref="CacheHandler"/> — in-memory caching (<see cref="CacheAttribute"/>)</item>
    ///   <item><see cref="MetricsHandler"/> — call count / duration / error counters (<see cref="MetricsAttribute"/>)</item>
    ///   <item><see cref="TimeoutHandler"/> — execution time limit (<see cref="TimeoutAttribute"/>)</item>
    ///   <item><see cref="DebounceHandler"/> — quiet-period delay (<see cref="DebounceAttribute"/>)</item>
    ///   <item><see cref="ThrottleHandler"/> — rate limiting (<see cref="ThrottleAttribute"/>)</item>
    /// </list>
    /// <see cref="AuthorizeHandler"/> is also registered but requires an
    /// <see cref="IAuthorizationProvider"/> implementation in DI to function.
    /// Custom handlers (your own <see cref="IAspectHandler"/> implementations) still need to
    /// be registered explicitly — this method only wires the built-ins shipped by the package.
    /// Pairs with <c>app.Services.UseAop()</c> which bridges DI into the aspect runtime.
    /// </summary>
    public static IServiceCollection AddAop(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<TraceHandler>();
        services.TryAddSingleton<RetryHandler>();
        services.TryAddSingleton<CacheHandler>();
        services.TryAddSingleton<MetricsHandler>(sp =>
        {
            var factory = sp.GetService<System.Diagnostics.Metrics.IMeterFactory>();
            return factory is not null ? new MetricsHandler(factory) : new MetricsHandler();
        });
        services.TryAddSingleton<TimeoutHandler>();
        services.TryAddSingleton<ValidateHandler>();
        services.TryAddSingleton<TransactionHandler>();

        services.TryAddSingleton<DebounceHandler>();
        services.TryAddSingleton<ThrottleHandler>();

        // AuthorizeHandler requires IAuthorizationProvider — only register if
        // the provider is already in DI, otherwise ValidateOnBuild (default in
        // ASP.NET Core) throws even when nobody uses [Authorize].
        if (services.Any(d => d.ServiceType == typeof(IAuthorizationProvider)))
            services.TryAddSingleton<AuthorizeHandler>();

        return services;
    }
}
