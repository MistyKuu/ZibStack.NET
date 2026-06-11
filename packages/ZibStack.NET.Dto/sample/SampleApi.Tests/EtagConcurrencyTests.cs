using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ZibStack.NET.Dto.Sample.Tests;

/// <summary>
/// Document is marked [CrudApi(Concurrency = true)] — endpoints carry weak ETags,
/// PATCH demands If-Match (428 without, 412 on stale) and DELETE honors If-Match.
/// </summary>
public class EtagConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EtagConcurrencyTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    private async Task<(string location, string etag)> CreateDocumentAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/documents",
            new { Title = $"Doc_{Guid.NewGuid():N}", Content = "v1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        return (response.Headers.Location!.ToString(), response.Headers.ETag!.ToString());
    }

    private Task<HttpResponseMessage> PatchAsync(string url, object body, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(req);
    }

    [Fact]
    public async Task GetById_ReturnsWeakETag()
    {
        var (location, _) = await CreateDocumentAsync();
        var response = await _client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.ETag!.IsWeak, "ETag should be weak (W/ prefix)");
    }

    [Fact]
    public async Task Patch_WithoutIfMatch_Returns428()
    {
        var (location, _) = await CreateDocumentAsync();
        var response = await PatchAsync(location, new { Title = "updated" }, ifMatch: null);
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithWrongETag_Returns412()
    {
        var (location, _) = await CreateDocumentAsync();
        var response = await PatchAsync(location, new { Title = "updated" }, ifMatch: "W/\"99999\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithCurrentETag_Succeeds_AndBumpsVersion()
    {
        var (location, etag) = await CreateDocumentAsync();

        var response = await PatchAsync(location, new { Title = "updated once" }, ifMatch: etag);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newEtag = response.Headers.ETag!.ToString();
        Assert.NotEqual(etag, newEtag);

        // The old ETag is now stale — a second writer using it must get 412.
        var stale = await PatchAsync(location, new { Title = "lost update" }, ifMatch: etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // The fresh ETag works.
        var fresh = await PatchAsync(location, new { Title = "updated twice" }, ifMatch: newEtag);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task Patch_WithStar_BypassesVersionCheck()
    {
        var (location, _) = await CreateDocumentAsync();
        var response = await PatchAsync(location, new { Title = "force overwrite" }, ifMatch: "*");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithStaleETag_Returns412()
    {
        var (location, etag) = await CreateDocumentAsync();
        // Bump the version so the captured ETag goes stale.
        var patch = await PatchAsync(location, new { Title = "bump" }, ifMatch: etag);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Delete, location);
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithCurrentETag_Succeeds()
    {
        var (location, etag) = await CreateDocumentAsync();
        var req = new HttpRequestMessage(HttpMethod.Delete, location);
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutIfMatch_StillSucceeds()
    {
        // If-Match is optional on DELETE — validated only when provided.
        var (location, _) = await CreateDocumentAsync();
        var response = await _client.DeleteAsync(location);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
