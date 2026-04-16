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

    /// <summary>
    /// Zod schemas (<c>.schema.ts</c> files). Each class becomes
    /// <c>export const {Name}Schema = z.object({…});</c> plus
    /// <c>export type {Name} = z.infer&lt;typeof {Name}Schema&gt;;</c> — the schema
    /// is the runtime validator, the derived type is the structural TS view.
    /// Independent from <see cref="TypeScript"/>: both can coexist (recommended)
    /// or run standalone — Zod alone gives you schemas + inferred types without
    /// a separate interface file.
    /// </summary>
    Zod = 1 << 3,
}
