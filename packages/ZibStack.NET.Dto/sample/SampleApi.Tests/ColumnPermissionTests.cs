using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ZibStack.NET.Dto.Sample.Tests;

/// <summary>
/// Player is marked [ColumnPermission("Salary", "finance.read")] — the generated
/// endpoints must null Salary for callers without that permission and pass it
/// through for callers that hold it.
/// </summary>
public class ColumnPermissionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _anonymous;

    public ColumnPermissionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
    }

    private HttpClient CreatePrivilegedClient() =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new ClaimInjectionFilter())))
        .CreateClient();

    private async Task<int> CreatePlayerAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/players",
            new { Name = name, Level = 10, Salary = 1234.56m, Password = "secret-pass" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task GetById_Anonymous_MasksSalary()
    {
        var id = await CreatePlayerAsync(_anonymous, $"PermGet_{Guid.NewGuid():N}");

        var body = await _anonymous.GetFromJsonAsync<JsonElement>($"/api/players/{id}");
        Assert.Equal(0m, body.GetProperty("salary").GetDecimal());
    }

    [Fact]
    public async Task Create_Response_Anonymous_MasksSalary()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/players",
            new { Name = $"PermCreate_{Guid.NewGuid():N}", Level = 10, Salary = 999m, Password = "secret-pass" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, body.GetProperty("salary").GetDecimal());
    }

    [Fact]
    public async Task GetList_Anonymous_MasksSalary()
    {
        var name = $"PermList_{Guid.NewGuid():N}";
        await CreatePlayerAsync(_anonymous, name);

        var body = await _anonymous.GetFromJsonAsync<JsonElement>($"/api/players?filter=Name=*{name}");
        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Filter should find the player we created");
        foreach (var item in items.EnumerateArray())
            Assert.Equal(0m, item.GetProperty("salary").GetDecimal());
    }

    [Fact]
    public async Task Select_Anonymous_DropsRestrictedField()
    {
        var name = $"PermSelect_{Guid.NewGuid():N}";
        await CreatePlayerAsync(_anonymous, name);

        var body = await _anonymous.GetFromJsonAsync<JsonElement>(
            $"/api/players?filter=Name=*{name}&select=Name,Salary");
        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Filter should find the player we created");
        foreach (var item in items.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("name", out _), "Selected non-restricted field should be present");
            Assert.False(item.TryGetProperty("salary", out _), "Restricted field should be dropped from select projection");
        }
    }

    [Fact]
    public async Task GetById_WithFinanceRead_SeesSalary()
    {
        var client = CreatePrivilegedClient();
        var id = await CreatePlayerAsync(client, $"PermPriv_{Guid.NewGuid():N}");

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/players/{id}");
        Assert.Equal(1234.56m, body.GetProperty("salary").GetDecimal());
    }

    [Fact]
    public async Task GetList_WithFinanceRead_SeesSalary()
    {
        var client = CreatePrivilegedClient();
        var name = $"PermPrivList_{Guid.NewGuid():N}";
        await CreatePlayerAsync(client, name);

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/players?filter=Name=*{name}");
        var items = body.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0, "Filter should find the player we created");
        foreach (var item in items.EnumerateArray())
            Assert.Equal(1234.56m, item.GetProperty("salary").GetDecimal());
    }

    /// <summary>Injects a principal holding the finance.read permission claim ahead of the app pipeline.</summary>
    private sealed class ClaimInjectionFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, nextMw) =>
            {
                var identity = new ClaimsIdentity("TestAuth");
                identity.AddClaim(new Claim("permission", "finance.read"));
                ctx.User = new ClaimsPrincipal(identity);
                await nextMw();
            });
            next(app);
        };
    }
}
