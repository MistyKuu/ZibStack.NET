using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// Standalone generator for [AspectHandler]-based runtime aspects.
/// Consuming packages that need inline emitters should create their own
/// IIncrementalGenerator and use AopPipeline.Register() with their emitters.
/// </summary>
[Generator]
public sealed class AopStandaloneGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // No inline emitters — only runtime handlers via [AspectHandler]
        AopPipeline.Register(context, new Dictionary<string, IAspectEmitter>());
    }
}
