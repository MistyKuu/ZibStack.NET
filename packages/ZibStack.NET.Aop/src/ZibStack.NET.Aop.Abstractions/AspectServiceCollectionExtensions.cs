using System;
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
    /// Registers the built-in ZibStack.NET aspect handlers in DI. Currently:
    /// <list type="bullet">
    ///   <item><see cref="TraceHandler"/> — for <see cref="TraceAttribute"/>.</item>
    /// </list>
    /// Custom handlers (your own <see cref="IAspectHandler"/> implementations) still need to
    /// be registered explicitly — this method only wires the built-ins shipped by the package.
    /// Pairs with <c>app.Services.UseAop()</c> which bridges DI into the aspect runtime.
    /// </summary>
    public static IServiceCollection AddAop(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<TraceHandler>();

        return services;
    }
}
