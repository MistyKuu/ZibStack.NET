using System;

namespace ZibStack.NET.TypeGen;

/// <summary>
/// Output formats the generator can produce. Combine via bitwise OR on
/// <see cref="GenerateTypesAttribute.Targets"/> to emit multiple at once
/// (e.g. <c>TypeTarget.TypeScript | TypeTarget.OpenApi</c>).
/// </summary>
[Flags]
public enum TypeTarget
{
    None = 0,

    /// <summary>TypeScript interfaces / types (file extension <c>.ts</c>).</summary>
    TypeScript = 1 << 0,

    /// <summary>OpenAPI 3.1 schema document (default extension <c>.yaml</c>).</summary>
    OpenApi = 1 << 1,

    /// <summary>
    /// Python <c>Pydantic v2</c> <c>BaseModel</c> classes (file extension <c>.py</c>).
    /// Idiomatic target for FastAPI backends consuming the same contract as the
    /// C# DTOs — type hints + optional validation on parse.
    /// </summary>
    Python = 1 << 2,
}
