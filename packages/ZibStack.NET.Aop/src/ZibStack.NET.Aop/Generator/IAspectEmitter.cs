using System.Collections.Generic;
using System.Text;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// Interface for compile-time inline aspect code emitters.
/// Each aspect that wants zero-overhead inline code generation implements this.
/// </summary>
public interface IAspectEmitter
{
    /// <summary>The fully qualified name of the attribute this emitter handles.</summary>
    string AttributeFullName { get; }

    /// <summary>
    /// Called exactly once before any <c>EmitClassMembers</c>/<c>EmitBefore</c>/… calls for
    /// a given class-emission. Emitters use this to reset per-class state (e.g. the
    /// "already emitted shared fields for this class" flag, event-id counters). Without
    /// this hook, mutable instance state persists across incremental generator re-runs,
    /// which manifests as dropped fields like <c>__cachedLogger</c> in the second run.
    /// </summary>
    void BeginClass(InterceptedClassModel classModel);

    /// <summary>Emit class-level declarations (fields, UnsafeAccessors, delegates, etc.)</summary>
    void EmitClassMembers(StringBuilder sb, InterceptedClassModel classModel, InterceptedMethodModel method, AspectInfo aspect, string indent);

    /// <summary>Emit code before the original method call.</summary>
    void EmitBefore(StringBuilder sb, InterceptedClassModel classModel, InterceptedMethodModel method, AspectInfo aspect, string indent);

    /// <summary>Emit code after the original method returns successfully.</summary>
    void EmitAfter(StringBuilder sb, InterceptedClassModel classModel, InterceptedMethodModel method, AspectInfo aspect, string indent);

    /// <summary>Emit code in the catch block when an exception occurs.</summary>
    void EmitOnException(StringBuilder sb, InterceptedClassModel classModel, InterceptedMethodModel method, AspectInfo aspect, string indent);

    /// <summary>Additional using directives this emitter requires.</summary>
    IEnumerable<string> RequiredUsings { get; }
}
