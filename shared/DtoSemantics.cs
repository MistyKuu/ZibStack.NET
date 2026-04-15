// ─────────────────────────────────────────────────────────────────────────────
// Shared between ZibStack.NET.Dto (the generator that creates Create/Update/
// Response DTOs) and ZibStack.NET.TypeGen (the generator that synthesizes the
// matching OpenAPI schemas). Linked into both assemblies via <Compile Include>
// — physically one file, logically the single source of truth.
//
// **Do not duplicate.** If you change a filter rule or add a new DtoTarget,
// both generators pick it up automatically. Introducing a second copy defeats
// the purpose — Dto emits one set of properties, TypeGen emits a different set,
// and the OpenAPI $refs silently drift out of sync with the real runtime DTOs.
//
// Namespace is deliberately generator-internal (one per consuming assembly).
// ─────────────────────────────────────────────────────────────────────────────

using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZibStack.NET.Shared;

/// <summary>
/// DTO variant this property could appear in. Values MUST match the ones Dto's
/// <c>ZibStack.NET.Dto.DtoTarget</c> attribute emits into user code — they're
/// read straight off <c>AttributeData.ConstructorArguments</c> as <c>int</c>.
/// </summary>
[System.Flags]
internal enum DtoTarget
{
    None     = 0,
    Create   = 1,
    Update   = 2,
    Response = 4,
    Query    = 8,
    List     = 16,
    All      = Create | Update | Response | Query | List,
}

/// <summary>
/// DTO filtering predicate shared by Dto's generator and TypeGen's synthesis.
/// Given the raw attribute values off a property, tells you whether a given
/// DTO variant should include that property.
/// </summary>
internal static class DtoSemantics
{
    // Attribute FQNs used by both generators — single point to maintain if Dto
    // ever moves/renames them.
    public const string DtoIgnoreAttr = "ZibStack.NET.Dto.DtoIgnoreAttribute";
    public const string DtoOnlyAttr = "ZibStack.NET.Dto.DtoOnlyAttribute";
    public const string DtoNameAttr = "ZibStack.NET.Dto.DtoNameAttribute";
    public const string DtoTargetType = "ZibStack.NET.Dto.DtoTarget";
    public const string CreateDtoAttr = "ZibStack.NET.Dto.CreateDtoAttribute";
    public const string UpdateDtoAttr = "ZibStack.NET.Dto.UpdateDtoAttribute";
    public const string ResponseDtoAttr = "ZibStack.NET.Dto.ResponseDtoAttribute";
    public const string CrudApiAttr = "ZibStack.NET.Dto.CrudApiAttribute";

    /// <summary>
    /// Reads <c>[DtoIgnore(...)]</c> / <c>[DtoOnly(...)]</c> off a property. Returns
    /// the bitmask pair that drives <see cref="IsIncluded"/>. Both zero = property
    /// goes into every DTO by default.
    /// </summary>
    public static (int IgnoreTargets, int OnlyTargets) ReadTargets(ISymbol property)
    {
        int ignore = 0, only = 0;
        foreach (var attr in property.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name == DtoIgnoreAttr)
            {
                // Zero-arg ctor → ignore everywhere (DtoTarget.All).
                if (attr.ConstructorArguments.Length == 0)
                    ignore = (int)DtoTarget.All;
                else if (attr.ConstructorArguments[0].Value is int i)
                    ignore = i;
            }
            else if (name == DtoOnlyAttr)
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int i)
                    only = i;
            }
        }
        return (ignore, only);
    }

    /// <summary>
    /// The single filter predicate. Mirrors <c>DtoPropertyInfo.IsIgnoredFrom</c>
    /// (which now delegates here) — <b>do not</b> reimplement elsewhere.
    /// Rules:
    /// <list type="bullet">
    ///   <item><c>[DtoIgnore]</c> (any form) wins over <c>[DtoOnly]</c>.</item>
    ///   <item><c>[DtoIgnore(flags)]</c> → property excluded from variants in <paramref name="variant"/> ∩ flags.</item>
    ///   <item><c>[DtoOnly(flags)]</c> → property excluded from variants NOT in flags.</item>
    ///   <item>Neither set → property included everywhere.</item>
    /// </list>
    /// </summary>
    public static bool IsIncluded(int ignoreTargets, int onlyTargets, DtoTarget variant)
    {
        var v = (int)variant;
        if (ignoreTargets != 0) return (ignoreTargets & v) == 0;
        if (onlyTargets != 0) return (onlyTargets & v) != 0;
        return true;
    }

    /// <summary>Convenience overload for callers that work directly with symbols.</summary>
    public static bool IsIncluded(ISymbol property, DtoTarget variant)
    {
        var (ignore, only) = ReadTargets(property);
        return IsIncluded(ignore, only, variant);
    }

    /// <summary>
    /// Default DTO type name convention matching the Dto generator's output:
    /// <c>Create{X}Request</c>, <c>Update{X}Request</c>, <c>{X}Response</c>.
    /// <see cref="DtoNameAttr"/> on the class overrides per variant (not yet read
    /// here — when support is added it lives in this one place).
    /// </summary>
    public static string GetDefaultDtoName(string className, DtoTarget variant) => variant switch
    {
        DtoTarget.Create => $"Create{className}Request",
        DtoTarget.Update => $"Update{className}Request",
        DtoTarget.Response => $"{className}Response",
        DtoTarget.Query => $"{className}Query",
        _ => className,
    };

    /// <summary>
    /// True when the class carries any attribute that makes the Dto generator
    /// emit a companion DTO (<c>[CrudApi]</c> implies all; explicit per-kind
    /// attrs opt in individually).
    /// </summary>
    public static bool HasDtoAttributeFor(INamedTypeSymbol cls, DtoTarget variant)
    {
        foreach (var attr in cls.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name == CrudApiAttr) return true;   // [CrudApi] implies all four
            if (variant == DtoTarget.Create && name == CreateDtoAttr) return true;
            if (variant == DtoTarget.Update && name == UpdateDtoAttr) return true;
            if (variant == DtoTarget.Response && name == ResponseDtoAttr) return true;
        }
        return false;
    }
}
