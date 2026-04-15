using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using ZibStack.NET.Shared;

namespace ZibStack.NET.Dto;

/// <summary>
/// Adapters that turn a <see cref="DtoConfiguratorParser.TypeConfig"/> into a
/// <see cref="DtoClassInfo"/> via the shared <c>BuildDtoClassInfoCore</c>. This
/// is Phase 1 — Create/Update/Combined only. ResponseDto / QueryDto fluent
/// support arrives in Phase 1B once their <c>Get*Info</c> bodies are split into
/// reusable cores too.
/// </summary>
public partial class DtoGenerator
{
    private static DtoClassInfo? BuildDtoClassInfoFromFluent(
        INamedTypeSymbol symbol, DtoKind kind,
        DtoConfiguratorParser.TypeConfig tc, HashSet<string> _)
    {
        string? name = kind switch
        {
            DtoKind.Create => tc.CreateName,
            DtoKind.Update => tc.UpdateName,
            DtoKind.Combined => tc.CreateOrUpdateName,
            _ => null,
        };
        string? createValidator = kind == DtoKind.Combined ? tc.CreateOrUpdateCreateValidator
            : kind == DtoKind.Create ? tc.CreateValidatorTypeName : null;
        string? updateValidator = kind == DtoKind.Combined ? tc.CreateOrUpdateUpdateValidator
            : kind == DtoKind.Update ? tc.UpdateValidatorTypeName : null;

        var info = BuildDtoClassInfoCore(symbol, kind, name, createValidator, updateValidator);
        if (info is null) return null;
        ApplyFluentPropertyOverrides(info, kind, tc);
        return info;
    }

    /// <summary>
    /// Applies <c>b.ForType&lt;T&gt;().Property(p =&gt; p.X).Ignore()/.IgnoreIn()/.OnlyIn()/.RenameTo()</c>
    /// overrides on top of the attribute-derived property list. Mutates <paramref name="info"/>'s
    /// Properties in place. Note: in Phase 1C this only runs on the fluent-driven extraction
    /// path — properties of types that pick up DTOs via attribute markers (like <c>[CrudApi]</c>)
    /// don't see fluent overrides yet.
    /// </summary>
    private static void ApplyFluentPropertyOverrides(DtoClassInfo info, DtoKind kind, DtoConfiguratorParser.TypeConfig tc)
    {
        if (tc.Properties.Count == 0) return;
        var variant = kind switch
        {
            DtoKind.Create => DtoTarget.Create,
            DtoKind.Update => DtoTarget.Update,
            DtoKind.Combined => DtoTarget.Create | DtoTarget.Update,
            _ => DtoTarget.None,
        };

        var rebuilt = new List<DtoPropertyInfo>(info.Properties.Count);
        foreach (var prop in info.Properties)
        {
            if (!tc.Properties.TryGetValue(prop.PropertyName, out var pc))
            {
                rebuilt.Add(prop);
                continue;
            }

            // Ignore semantics — variant intersected with fluent IgnoreTargets/OnlyTargets.
            // Same predicate as DtoSemantics.IsIncluded so we stay consistent with TypeGen.
            var included = DtoSemantics.IsIncluded(pc.IgnoreTargets, pc.OnlyTargets,
                kind == DtoKind.Combined ? DtoTarget.Create : variant);
            if (!included) continue;

            // RenameTo → rebuild DtoPropertyInfo with new PropertyName (and matching JsonName).
            if (pc.RenameTo is { Length: > 0 } newName)
                rebuilt.Add(new DtoPropertyInfo(
                    newName, CamelCaseSafe(newName), prop.DisplayTypeName,
                    prop.IsNullable, prop.IsRequired, prop.IsValueType,
                    prop.IgnoreTargets, 0,
                    sourcePropertyName: prop.SourcePropertyName,
                    validationAttributes: prop.ValidationAttributes,
                    validationRules: prop.ValidationRules)
                {
                    NestedCreateDtoName = prop.NestedCreateDtoName,
                    NestedUpdateDtoName = prop.NestedUpdateDtoName,
                    FlattenEntityPath = prop.FlattenEntityPath,
                });
            else
                rebuilt.Add(prop);
        }

        info.Properties.Clear();
        info.Properties.AddRange(rebuilt);
    }

    /// <summary>JSON property names follow camelCase by convention.</summary>
    private static string CamelCaseSafe(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private static ResponseDtoInfo? BuildResponseDtoInfoFromFluent(
        INamedTypeSymbol symbol, DtoConfiguratorParser.TypeConfig tc) =>
        BuildResponseDtoInfoCore(symbol, tc.ResponseName);

    private static QueryDtoInfo? BuildQueryDtoInfoFromFluent(
        INamedTypeSymbol symbol, DtoConfiguratorParser.TypeConfig tc) =>
        BuildQueryDtoInfoCore(symbol, tc.QueryName, tc.QuerySortable, tc.QueryDefaultSort, tc.QueryDefaultSortDirection);
}
