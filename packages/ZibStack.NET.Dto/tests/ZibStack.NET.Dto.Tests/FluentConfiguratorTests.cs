using System;
using System.Linq;
using System.Reflection;

namespace ZibStack.NET.Dto.Tests;

/// <summary>
/// End-to-end tests for the fluent <c>IDtoConfigurator</c> DSL. Models live in
/// Models.cs (<c>FluentArticle</c>, <c>FluentMixedModel</c>); the fluent config is
/// in <see cref="FluentTestDtoConfig"/>. Tests assert on the actual generator
/// output by reflecting over the types it produced — if the wiring breaks, these
/// fail with a missing-type or missing-property exception, not a string-match.
/// </summary>
public class FluentConfiguratorTests
{
    // ── Pure fluent (no Dto attributes on model) ────────────────────────

    [Fact]
    public void FluentArticle_CreateDto_GeneratedWithCustomName()
    {
        // Custom name set via b.ForType<FluentArticle>().CreateDto(opts => opts.Name = "FluentArticleCreate").
        // Default would have been "CreateFluentArticleRequest".
        var t = FindType("FluentArticleCreate");
        Assert.NotNull(t);
    }

    [Fact]
    public void FluentArticle_UpdateDto_GeneratedWithDefaultName()
    {
        var t = FindType("UpdateFluentArticleRequest");
        Assert.NotNull(t);
    }

    [Fact]
    public void FluentArticle_ResponseDto_GeneratedWithDefaultName()
    {
        var t = FindType("FluentArticleResponse");
        Assert.NotNull(t);
    }

    [Fact]
    public void FluentArticle_QueryDto_GeneratedWithDefaultName()
    {
        var t = FindType("FluentArticleQuery");
        Assert.NotNull(t);
    }

    // ── Per-property fluent overrides ───────────────────────────────────

    [Fact]
    public void IgnoreIn_RemovesPropertyFromCorrectVariant()
    {
        // .Property(p => p.Id).IgnoreIn(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)
        // → Id absent in Create / Update / Query, present in Response.
        Assert.Null(GetProperty("FluentArticleCreate", "Id"));
        Assert.Null(GetProperty("UpdateFluentArticleRequest", "Id"));
        Assert.Null(GetProperty("FluentArticleQuery", "Id"));
        Assert.NotNull(GetProperty("FluentArticleResponse", "Id"));
    }

    [Fact]
    public void IgnoreIn_RespectsCreateOnlyExclusion()
    {
        // .Property(p => p.PublishedAt).IgnoreIn(DtoTarget.Create) → absent in Create only.
        Assert.Null(GetProperty("FluentArticleCreate", "PublishedAt"));
        Assert.NotNull(GetProperty("UpdateFluentArticleRequest", "PublishedAt"));
        Assert.NotNull(GetProperty("FluentArticleResponse", "PublishedAt"));
    }

    [Fact]
    public void RenameTo_RenamesPropertyOnDtoButPreservesEntityMapping()
    {
        // .Property(p => p.Body).RenameTo("content") → emitted as "content" on Create/Update/Response.
        // The Query side intentionally skips rename (Phase 1D limitation — Query expr trees use the name).
        Assert.NotNull(GetProperty("FluentArticleCreate", "content"));
        Assert.Null(GetProperty("FluentArticleCreate", "Body"));
        Assert.NotNull(GetProperty("UpdateFluentArticleRequest", "content"));
        Assert.NotNull(GetProperty("FluentArticleResponse", "content"));
    }

    [Fact]
    public void Response_FromEntity_MapsRenamedPropertyToOriginalSource()
    {
        // The response DTO must be constructible via FromEntity(FluentArticle) — this
        // catches the bug where a fluent rename wired entity.{newName} (compile error)
        // instead of entity.{originalName}.
        var responseType = FindType("FluentArticleResponse");
        Assert.NotNull(responseType);
        var fromEntity = responseType!.GetMethod("FromEntity",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(FluentArticle) }, null);
        Assert.NotNull(fromEntity);

        var article = new FluentArticle { Id = 7, Title = "Hello", Body = "World", PublishedAt = DateTime.UtcNow };
        var resp = fromEntity!.Invoke(null, new object[] { article });
        Assert.NotNull(resp);
        // 'content' on the response should hold the original entity.Body value.
        var contentProp = resp!.GetType().GetProperty("content");
        Assert.NotNull(contentProp);
        Assert.Equal("World", contentProp!.GetValue(resp));
    }

    // ── [CrudApi] attribute marker + fluent options + per-property override ──
    //
    // Note: the test assembly doesn't reference Microsoft.AspNetCore — so the actual
    // endpoint-class emission is skipped (pipeline gates on hasAspNetCore). The
    // implied-DTO emission still runs, which is what we exercise here. End-to-end
    // verification of CrudApi options + endpoint routing lives in the SampleApi
    // build (sample/SampleApi/DtoConfig.cs sets api/v3/mixed and we eyeball the
    // generated Endpoints.g.cs there).

    [Fact]
    public void CrudApi_PerPropertyIgnore_AppliesToImpliedDtos()
    {
        // .Property(p => p.Secret).Ignore() — Secret should be absent in every
        // DTO variant emitted via the [CrudApi]-implied pipeline.
        Assert.Null(GetProperty("CreateFluentMixedModelRequest", "Secret"));
        Assert.Null(GetProperty("UpdateFluentMixedModelRequest", "Secret"));
        Assert.Null(GetProperty("FluentMixedModelResponse", "Secret"));
        // Sanity: Name (the not-ignored property) is present.
        Assert.NotNull(GetProperty("CreateFluentMixedModelRequest", "Name"));
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static Type? FindType(string typeName) =>
        typeof(FluentArticle).Assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);

    private static PropertyInfo? GetProperty(string typeName, string propertyName)
    {
        var t = FindType(typeName);
        return t?.GetProperty(propertyName);
    }
}
