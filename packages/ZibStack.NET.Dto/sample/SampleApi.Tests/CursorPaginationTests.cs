using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ZibStack.NET.Dto.Sample.Tests;

/// <summary>
/// Generated GET list endpoints support keyset pagination: <c>?cursor=</c> (empty)
/// starts from the beginning, the response carries an opaque <c>nextCursor</c> which
/// is null on the last page. Items come back in stable key order.
/// </summary>
public class CursorPaginationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CursorPaginationTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    private async Task<List<int>> SeedDocumentsAsync(string prefix, int count)
    {
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/documents",
                new { Title = $"{prefix}_{i}", Content = "x" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            ids.Add(body.GetProperty("id").GetInt32());
        }
        return ids;
    }

    [Fact]
    public async Task Cursor_WalksAllPages_InKeyOrder_WithoutDuplicates()
    {
        var prefix = $"Cur_{Guid.NewGuid():N}";
        var seeded = await SeedDocumentsAsync(prefix, 5);

        var collected = new List<int>();
        string cursorQuery = "cursor=";
        for (var hop = 0; hop < 10; hop++) // bound the loop in case nextCursor never ends
        {
            var page = await _client.GetFromJsonAsync<JsonElement>(
                $"/api/documents?filter=Title=*{prefix}&pageSize=2&{cursorQuery}");
            foreach (var item in page.GetProperty("items").EnumerateArray())
                collected.Add(item.GetProperty("id").GetInt32());

            var next = page.GetProperty("nextCursor");
            if (next.ValueKind == JsonValueKind.Null) break;
            cursorQuery = $"cursor={Uri.EscapeDataString(next.GetString()!)}";
        }

        Assert.Equal(seeded.OrderBy(i => i), collected); // all items, key order, no dupes
    }

    [Fact]
    public async Task Cursor_LastPage_HasNullNextCursor()
    {
        var prefix = $"CurLast_{Guid.NewGuid():N}";
        await SeedDocumentsAsync(prefix, 2);

        var page = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/documents?filter=Title=*{prefix}&pageSize=10&cursor=");
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Cursor_RespectsFilter()
    {
        var prefix = $"CurFil_{Guid.NewGuid():N}";
        await SeedDocumentsAsync(prefix, 3);
        await SeedDocumentsAsync($"Other_{Guid.NewGuid():N}", 2);

        var page = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/documents?filter=Title=*{prefix}&pageSize=50&cursor=");
        Assert.Equal(3, page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task NoCursorParam_KeepsOffsetPagination()
    {
        var prefix = $"CurOff_{Guid.NewGuid():N}";
        await SeedDocumentsAsync(prefix, 1);

        var page = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/documents?filter=Title=*{prefix}");
        // offset shape: totalCount/page/pageSize, no nextCursor
        Assert.True(page.TryGetProperty("totalCount", out _));
        Assert.False(page.TryGetProperty("nextCursor", out _));
    }
}
