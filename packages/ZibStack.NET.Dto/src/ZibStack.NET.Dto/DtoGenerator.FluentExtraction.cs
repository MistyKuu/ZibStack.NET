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

    /// <summary>
    /// Looks up a TypeConfig by reconstructed full name (<c>{Namespace}.{ClassName}</c>)
    /// and applies its per-property fluent overrides on <paramref name="info"/>. Used by
    /// the [CrudApi]-implied pipeline so users can mix [CrudApi] with fluent
    /// b.ForType&lt;T&gt;().Property(...) overrides.
    /// </summary>
    private static void ApplyFluentOverridesByClassName(DtoClassInfo info, DtoKind kind, DtoConfiguratorParser.Parsed? fluent)
    {
        if (fluent is null) return;
        var fullName = info.Namespace is null ? info.ClassName : $"{info.Namespace}.{info.ClassName}";
        if (!fluent.ByType.TryGetValue(fullName, out var tc)) return;
        ApplyFluentPropertyOverrides(info, kind, tc);
    }

    /// <summary>
    /// Looks up CrudApiInfo overrides from fluent <c>b.ForType&lt;T&gt;().CrudApi(opts =&gt; ...)</c>
    /// and applies them on the attribute-derived CrudApiInfo. Sentinel int.MinValue (unset)
    /// in TypeConfig.CrudOperations means "fluent didn't touch it" — keep the attribute value.
    /// </summary>
    private static void ApplyFluentCrudApiOverrides(CrudApiInfo info, DtoConfiguratorParser.Parsed? fluent)
    {
        if (fluent is null) return;
        var fullName = info.Namespace is null ? info.ClassName : $"{info.Namespace}.{info.ClassName}";
        if (!fluent.ByType.TryGetValue(fullName, out var tc) || !tc.HasCrudApiBlock) return;

        if (!string.IsNullOrEmpty(tc.CrudRoute)) info.Route = tc.CrudRoute!;
        // RoutePrefix isn't a separate field on CrudApiInfo (route is fully resolved at
        // attribute-read time) — splice fluent RoutePrefix into the existing route.
        if (!string.IsNullOrEmpty(tc.CrudRoutePrefix))
        {
            var bare = info.Route.TrimStart('/').Replace("api/", "");
            info.Route = $"api/{tc.CrudRoutePrefix!.Trim('/')}/{bare}";
        }
        if (!string.Equals(tc.CrudKeyProperty, "Id", System.StringComparison.Ordinal)) info.KeyPropertyName = tc.CrudKeyProperty;
        if (tc.CrudOperations != unchecked((int)0xFFFFFFFF)) info.Operations = tc.CrudOperations;
        if (tc.CrudStyle != 0) info.Style = tc.CrudStyle;
        if (tc.CrudAuthorizePolicy is { } a) info.AuthorizePolicy = a;
        if (tc.CrudGetByIdPolicy is { } g1) info.GetByIdPolicy = g1;
        if (tc.CrudGetListPolicy is { } g2) info.GetListPolicy = g2;
        if (tc.CrudCreatePolicy is { } c) info.CreatePolicy = c;
        if (tc.CrudUpdatePolicy is { } u) info.UpdatePolicy = u;
        if (tc.CrudDeletePolicy is { } d) info.DeletePolicy = d;
    }

    /// <summary>
    /// Applies fluent per-property overrides on a Response DTO. Filters out
    /// properties whose fluent config excludes <see cref="DtoTarget.Response"/>;
    /// renames properties with <c>.RenameTo()</c>. Mutates <c>info.Properties</c>
    /// (and the matching ListProperties) in place.
    /// </summary>
    private static void ApplyFluentResponseOverrides(ResponseDtoInfo info, DtoConfiguratorParser.Parsed? fluent)
    {
        if (fluent is null) return;
        var fullName = info.Namespace is null ? info.ClassName : $"{info.Namespace}.{info.ClassName}";
        if (!fluent.ByType.TryGetValue(fullName, out var tc) || tc.Properties.Count == 0) return;

        MergeResponseProps(info.Properties, tc);
        if (info.ListProperties is { } listProps)
            MergeResponseProps(listProps, tc);
    }

    private static void MergeResponseProps(List<ResponsePropertyInfo> props, DtoConfiguratorParser.TypeConfig tc)
    {
        var rebuilt = new List<ResponsePropertyInfo>(props.Count);
        foreach (var p in props)
        {
            // Match by source property name when present (post-rename safety), else PropertyName.
            var key = p.SourcePropertyName ?? p.PropertyName;
            if (!tc.Properties.TryGetValue(key, out var pc)) { rebuilt.Add(p); continue; }

            if (!DtoSemantics.IsIncluded(pc.IgnoreTargets, pc.OnlyTargets, DtoTarget.Response)) continue;

            if (pc.RenameTo is { Length: > 0 } newName)
            {
                // Preserve the ORIGINAL entity property name so the generator's
                // FromEntity()/ProjectFrom() can still read entity.{SourcePropertyName}.
                rebuilt.Add(new ResponsePropertyInfo(
                    newName, CamelCaseSafe(newName), p.DisplayTypeName, p.ValidationAttributes,
                    isNestedResponse: p.IsNestedResponse, isNullable: p.IsNullable,
                    sourceTypeName: p.SourceTypeName, nestedResponseName: p.NestedResponseName,
                    flattenSource: p.FlattenSource, flattenProjection: p.FlattenProjection,
                    sourcePropertyName: p.SourcePropertyName ?? p.PropertyName));
            }
            else rebuilt.Add(p);
        }
        props.Clear();
        props.AddRange(rebuilt);
    }

    /// <summary>
    /// Applies fluent per-property overrides on a Query DTO. Filters out properties
    /// whose fluent config excludes <see cref="DtoTarget.Query"/>; renames with
    /// <c>.RenameTo()</c>. Same locality rules as Response/Create/Update.
    /// </summary>
    private static void ApplyFluentQueryOverrides(QueryDtoInfo info, DtoConfiguratorParser.Parsed? fluent)
    {
        if (fluent is null) return;
        var fullName = info.Namespace is null ? info.ClassName : $"{info.Namespace}.{info.ClassName}";
        if (!fluent.ByType.TryGetValue(fullName, out var tc) || tc.Properties.Count == 0) return;

        var rebuilt = new List<QueryPropertyInfo>(info.Properties.Count);
        foreach (var p in info.Properties)
        {
            if (!tc.Properties.TryGetValue(p.PropertyName, out var pc)) { rebuilt.Add(p); continue; }
            if (!DtoSemantics.IsIncluded(pc.IgnoreTargets, pc.OnlyTargets, DtoTarget.Query)) continue;

            // Note: .RenameTo() is intentionally NOT applied to Query DTOs in Phase 1D —
            // the Query generator uses PropertyName both for the URL param and for the
            // x.{PropertyName} expression accessing the entity. Renaming would break
            // expression compilation. QueryPropertyInfo would need a SourcePropertyName
            // field (and generator changes) to support it.
            rebuilt.Add(p);
        }
        info.Properties.Clear();
        info.Properties.AddRange(rebuilt);
    }

    private static ResponseDtoInfo? BuildResponseDtoInfoFromFluent(
        INamedTypeSymbol symbol, DtoConfiguratorParser.TypeConfig tc) =>
        BuildResponseDtoInfoCore(symbol, tc.ResponseName);

    private static QueryDtoInfo? BuildQueryDtoInfoFromFluent(
        INamedTypeSymbol symbol, DtoConfiguratorParser.TypeConfig tc) =>
        BuildQueryDtoInfoCore(symbol, tc.QueryName, tc.QuerySortable, tc.QueryDefaultSort, tc.QueryDefaultSortDirection);
}
