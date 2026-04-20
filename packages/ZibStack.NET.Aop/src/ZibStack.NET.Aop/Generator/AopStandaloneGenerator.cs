using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// The single AOP generator that handles ALL aspects:
/// - Runtime handler-based aspects ([Trace], [Retry], [Cache], [Metrics], etc.)
/// - Inline emitter aspects ([Log])
/// - Apply() bulk rules for any of the above
/// </summary>
[Generator]
public sealed class AopStandaloneGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var emitters = new Dictionary<string, IAspectEmitter>
        {
            { "ZibStack.NET.Log.LogAttribute", new ZibStack.NET.Log.Generator.LogAspectEmitter() }
        };

        var classDataProviders = new List<IClassDataProvider>
        {
            new ZibStack.NET.Log.Generator.LogClassDataProvider()
        };

        AopPipeline.Register(context, emitters, classDataProviders);
    }
}
