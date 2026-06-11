using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZibStack.NET.Dto.Sample;
using ZibStack.NET.Dto.Sample.Models;

namespace ZibStack.NET.Dto.Sample.Tests;

/// <summary>
/// Document is marked [CrudApi(Audit = true)] — generated endpoints stamp
/// CreatedAt/UpdatedAt (UTC) and CreatedBy/UpdatedBy (identity name). Audit fields
/// live on the entity, not in the response DTO, so assertions read the DbContext.
/// </summary>
public class AuditFieldsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuditFieldsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static async Task<int> CreateDocumentAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/documents", new { Title = title, Content = "v1" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private Document LoadDocument(WebApplicationFactory<Program> factory, int id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Documents.AsNoTracking().Single(d => d.Id == id);
    }

    [Fact]
    public async Task Create_StampsCreatedAtAndUpdatedAt()
    {
        var client = _factory.CreateClient();
        var id = await CreateDocumentAsync(client, $"Audit_{Guid.NewGuid():N}");

        var doc = LoadDocument(_factory, id);
        Assert.NotEqual(default, doc.CreatedAt);
        Assert.NotEqual(default, doc.UpdatedAt);
        Assert.True((DateTime.UtcNow - doc.CreatedAt).Duration() < TimeSpan.FromMinutes(1));
        Assert.Null(doc.CreatedBy); // anonymous caller
    }

    [Fact]
    public async Task Patch_RefreshesUpdatedAt_KeepsCreatedAt()
    {
        var client = _factory.CreateClient();
        var id = await CreateDocumentAsync(client, $"Audit_{Guid.NewGuid():N}");
        var before = LoadDocument(_factory, id);

        await Task.Delay(50); // ensure a measurable clock difference
        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/documents/{id}")
        {
            Content = JsonContent.Create(new { Title = "updated title" })
        };
        req.Headers.TryAddWithoutValidation("If-Match", "*");
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = LoadDocument(_factory, id);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.True(after.UpdatedAt > before.UpdatedAt, "PATCH should refresh UpdatedAt");
    }

    [Fact]
    public async Task Create_AsAuthenticatedUser_StampsCreatedBy()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new NamedUserFilter("alice@example.com"))));
        var client = factory.CreateClient();

        var id = await CreateDocumentAsync(client, $"Audit_{Guid.NewGuid():N}");

        var doc = LoadDocument(factory, id);
        Assert.Equal("alice@example.com", doc.CreatedBy);
        Assert.Equal("alice@example.com", doc.UpdatedBy);
    }

    /// <summary>Injects an authenticated principal with a name claim ahead of the app pipeline.</summary>
    private sealed class NamedUserFilter : IStartupFilter
    {
        private readonly string _name;
        public NamedUserFilter(string name) => _name = name;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, nextMw) =>
            {
                var identity = new ClaimsIdentity("TestAuth");
                identity.AddClaim(new Claim(ClaimTypes.Name, _name));
                ctx.User = new ClaimsPrincipal(identity);
                await nextMw();
            });
            next(app);
        };
    }
}
