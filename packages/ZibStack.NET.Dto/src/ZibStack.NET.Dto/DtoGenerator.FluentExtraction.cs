using System.Collections.Generic;
using Microsoft.CodeAnalysis;

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

        return BuildDtoClassInfoCore(symbol, kind, name, createValidator, updateValidator);
    }

    private static ResponseDtoInfo? BuildResponseDtoInfoFromFluent(
        INamedTypeSymbol symbol, DtoConfiguratorParser.TypeConfig tc) =>
        BuildResponseDtoInfoCore(symbol, tc.ResponseName);

    private static QueryDtoInfo? BuildQueryDtoInfoFromFluent(
        INamedTypeSymbol symbol, DtoConfiguratorParser.TypeConfig tc) =>
        BuildQueryDtoInfoCore(symbol, tc.QueryName, tc.QuerySortable, tc.QueryDefaultSort, tc.QueryDefaultSortDirection);
}
