using ZibStack.NET.Dto;
using ZibStack.NET.Dto.Sample.Models;

namespace ZibStack.NET.Dto.Sample;

/// <summary>
/// Fluent project-wide DTO config. Implementing IDtoConfigurator triggers the
/// generator's fluent pipeline — each <c>b.ForType&lt;T&gt;().CreateDto()</c>
/// (or .UpdateDto / .CreateOrUpdateDto) is equivalent to putting the matching
/// attribute on the class. Lets you keep model files free of generation markers
/// and configure everything in one place.
///
/// <para>
/// Phase 1: only Create / Update / CreateOrUpdate go through fluent.
/// Response / Query / per-property overrides are still attribute-driven —
/// landing in Phase 1B once their extraction is split into reusable cores.
/// </para>
/// </summary>
// `internal` matches the generated IDtoConfigurator interface accessibility —
// the config is project-internal anyway, never exposed across assembly bounds.
internal sealed class DtoConfig : IDtoConfigurator
{
    public void Configure(IDtoBuilder b)
    {
        // Article has no [CreateDto]/[UpdateDto] attributes — fluent fully drives
        // its DTO generation. Validation attributes on the model still feed
        // through into the generated request DTOs.
        b.ForType<Article>()
            .CreateDto()
            .UpdateDto()
            .ResponseDto()
            .QueryDto(q => { q.DefaultSort = "PublishedAt"; q.DefaultSortDirection = SortDirection.Desc; })
            // Per-property fluent overrides (Phase 1C). Equivalent to scattering
            // [DtoIgnore]/[DtoName] across the model — kept centralized here instead.
            .Property(p => p.Id).IgnoreIn(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)
            .Property(p => p.Body).RenameTo("content")
            .Property(p => p.PublishedAt).IgnoreIn(DtoTarget.Create);
    }
}
