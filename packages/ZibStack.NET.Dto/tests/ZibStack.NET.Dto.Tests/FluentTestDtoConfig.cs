using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

/// <summary>
/// Project-wide fluent DTO config for the test assembly. Drives <c>FluentArticle</c>
/// (zero-attribute model) and configures <c>FluentMixedModel</c> (which keeps its
/// <c>[CrudApi]</c> marker but pulls options + per-property overrides from here).
/// </summary>
internal sealed class FluentTestDtoConfig : IDtoConfigurator
{
    public void Configure(IDtoBuilder b)
    {
        // Zero-attribute model — fluent fully drives all four DTO variants.
        b.ForType<FluentArticle>()
            .CreateDto(opts => opts.Name = "FluentArticleCreate")
            .UpdateDto()
            .ResponseDto()
            .QueryDto(q => { q.DefaultSort = "PublishedAt"; q.DefaultSortDirection = SortDirection.Desc; })
            .Property(p => p.Id).IgnoreIn(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)
            .Property(p => p.Body).RenameTo("content")
            .Property(p => p.PublishedAt).IgnoreIn(DtoTarget.Create);

        // Mixed-mode — [CrudApi] attribute provides the marker; fluent overrides
        // CrudApi options + adds a per-property override. Validates Phase 1D wiring
        // through the [CrudApi]-implied pipeline.
        b.ForType<FluentMixedModel>()
            .CrudApi(api =>
            {
                api.Operations = CrudOperations.Create | CrudOperations.GetById;
                api.Route = "api/v3/mixed";
            })
            .Property(p => p.Secret).Ignore();   // hide from every DTO variant
    }
}
