using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using ZibStack.NET.Query;

namespace SampleApi;

// Generic in-memory CRUD endpoint backing the playground Data tab + Examples "Run" button.
// Each collection is keyed by name (last segment of the user's apiUrl, e.g. /api/products -> "products").
// Filter/sort are evaluated through ZibStack.NET.Query's parsers over JsonObject, so the playground
// demonstrates the real DSL semantics — not a re-implementation.
public static class MockApiEndpoint
{
    private static readonly ConcurrentDictionary<string, Collection> Stores =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class Collection
    {
        public ConcurrentDictionary<int, JsonObject> Items { get; } = new();
        public int NextId;
    }

    public static void MapMockApi(this WebApplication app)
    {
        app.MapGet("/mock/{collection}", (string collection, HttpContext ctx) =>
            List(collection, ctx.Request.Query));

        app.MapGet("/mock/{collection}/{id:int}", (string collection, int id) =>
            GetOne(collection, id));

        app.MapPost("/mock/{collection}", async (string collection, HttpContext ctx) =>
            await Create(collection, ctx));

        app.MapMethods("/mock/{collection}/{id:int}", new[] { "PATCH", "PUT" },
            async (string collection, int id, HttpContext ctx) => await Update(collection, id, ctx));

        app.MapDelete("/mock/{collection}/{id:int}", (string collection, int id) =>
            Delete(collection, id));

        // Bulk seed — used by FE to populate a collection on first sighting.
        app.MapPost("/mock/{collection}/seed", async (string collection, HttpContext ctx) =>
            await Seed(collection, ctx));

        app.MapDelete("/mock/{collection}/clear", (string collection) =>
        {
            Stores.TryRemove(collection, out _);
            return Results.NoContent();
        });
    }

    // ─── Endpoints ────────────────────────────────────────────────────

    private static IResult List(string collection, IQueryCollection q)
    {
        var coll = Stores.GetOrAdd(collection, _ => new Collection());
        IEnumerable<JsonObject> items = coll.Items.Values;

        var filter = q["filter"].ToString();
        if (!string.IsNullOrEmpty(filter))
        {
            var expr = FilterParser.ParseExpression(filter);
            if (expr is not null) items = items.Where(i => EvalExpr(expr, i));
        }

        var sort = q["sort"].ToString();
        if (!string.IsNullOrEmpty(sort))
        {
            var clauses = SortParser.Parse(sort);
            IOrderedEnumerable<JsonObject>? ordered = null;
            foreach (var s in clauses)
            {
                ordered = ordered is null
                    ? (s.Descending
                        ? items.OrderByDescending(i => GetSortKey(i, s.Field))
                        : items.OrderBy(i => GetSortKey(i, s.Field)))
                    : (s.Descending
                        ? ordered.ThenByDescending(i => GetSortKey(i, s.Field))
                        : ordered.ThenBy(i => GetSortKey(i, s.Field)));
            }
            if (ordered is not null) items = ordered;
        }

        var arr = items.ToList();
        var total = arr.Count;

        var page = TryParseInt(q["page"], 1);
        var pageSize = TryParseInt(q["pageSize"], 20);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var paged = arr.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var select = q["select"].ToString();
        List<JsonNode?> projected = paged.Cast<JsonNode?>().ToList();
        if (!string.IsNullOrEmpty(select))
        {
            var fields = select.Split(',')
                .Select(f => f.Trim())
                .Where(f => f.Length > 0)
                .Select(ToCamelCase)
                .ToArray();
            projected = paged.Select(item =>
            {
                var p = new JsonObject();
                foreach (var f in fields)
                    if (item.TryGetPropertyValue(f, out var v))
                        p[f] = v?.DeepClone();
                return (JsonNode?)p;
            }).ToList();
        }

        return Results.Ok(new
        {
            items = projected,
            page,
            pageSize,
            totalCount = total,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    private static IResult GetOne(string collection, int id)
    {
        var coll = Stores.GetOrAdd(collection, _ => new Collection());
        return coll.Items.TryGetValue(id, out var item)
            ? Results.Ok(item)
            : Results.NotFound();
    }

    private static async Task<IResult> Create(string collection, HttpContext ctx)
    {
        var coll = Stores.GetOrAdd(collection, _ => new Collection());
        var body = (await JsonNode.ParseAsync(ctx.Request.Body)) as JsonObject ?? new JsonObject();
        var id = Interlocked.Increment(ref coll.NextId);
        body["id"] = id;
        coll.Items[id] = body;
        return Results.Created($"/mock/{collection}/{id}", body);
    }

    private static async Task<IResult> Update(string collection, int id, HttpContext ctx)
    {
        var coll = Stores.GetOrAdd(collection, _ => new Collection());
        if (!coll.Items.TryGetValue(id, out var existing)) return Results.NotFound();
        var patch = (await JsonNode.ParseAsync(ctx.Request.Body)) as JsonObject ?? new JsonObject();
        foreach (var kv in patch)
        {
            if (kv.Key == "id") continue;
            existing[kv.Key] = kv.Value?.DeepClone();
        }
        return Results.Ok(existing);
    }

    private static IResult Delete(string collection, int id)
    {
        var coll = Stores.GetOrAdd(collection, _ => new Collection());
        return coll.Items.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Seed(string collection, HttpContext ctx)
    {
        var coll = Stores.GetOrAdd(collection, _ => new Collection());
        if (!coll.Items.IsEmpty)
            return Results.Ok(new { seeded = false, count = coll.Items.Count });

        var body = (await JsonNode.ParseAsync(ctx.Request.Body)) as JsonArray ?? new JsonArray();
        var added = 0;
        foreach (var node in body)
        {
            if (node is not JsonObject obj) continue;
            var detached = (JsonObject)obj.DeepClone();
            var id = Interlocked.Increment(ref coll.NextId);
            detached["id"] = id;
            coll.Items[id] = detached;
            added++;
        }
        return Results.Ok(new { seeded = true, count = added });
    }

    // ─── Filter / sort eval over JsonObject ───────────────────────────

    private static bool EvalExpr(FilterExpression expr, JsonObject item) => expr switch
    {
        FilterLeaf leaf => EvalClause(leaf.Clause, item),
        FilterAnd and => EvalExpr(and.Left, item) && EvalExpr(and.Right, item),
        FilterOr or => EvalExpr(or.Left, item) || EvalExpr(or.Right, item),
        _ => true
    };

    private static bool EvalClause(FilterClause c, JsonObject item)
    {
        var raw = GetByPath(item, c.Field);
        var fieldStr = raw?.ToString() ?? "";
        var valStr = c.Value;
        var cmp = c.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        switch (c.Operator)
        {
            case FilterOperator.Equals: return ScalarEquals(raw, valStr, c.CaseInsensitive);
            case FilterOperator.NotEquals: return !ScalarEquals(raw, valStr, c.CaseInsensitive);
            case FilterOperator.Contains: return fieldStr.IndexOf(valStr, cmp) >= 0;
            case FilterOperator.NotContains: return fieldStr.IndexOf(valStr, cmp) < 0;
            case FilterOperator.StartsWith: return fieldStr.StartsWith(valStr, cmp);
            case FilterOperator.NotStartsWith: return !fieldStr.StartsWith(valStr, cmp);
            case FilterOperator.EndsWith: return fieldStr.EndsWith(valStr, cmp);
            case FilterOperator.NotEndsWith: return !fieldStr.EndsWith(valStr, cmp);
            case FilterOperator.GreaterThan:
            case FilterOperator.GreaterThanOrEqual:
            case FilterOperator.LessThan:
            case FilterOperator.LessThanOrEqual:
                return CompareOrdered(raw, valStr, c.Operator);
            case FilterOperator.In:
                return valStr.Split(';').Any(v => ScalarEquals(raw, v, c.CaseInsensitive));
            case FilterOperator.NotIn:
                return !valStr.Split(';').Any(v => ScalarEquals(raw, v, c.CaseInsensitive));
        }
        return false;
    }

    private static JsonNode? GetByPath(JsonObject item, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('.');
        JsonNode? cur = item;
        foreach (var part in parts)
        {
            if (cur is not JsonObject obj) return null;
            if (!obj.TryGetPropertyValue(ToCamelCase(part), out cur)) return null;
        }
        return cur;
    }

    private static bool ScalarEquals(JsonNode? node, string value, bool ci)
    {
        if (node is null) return string.IsNullOrEmpty(value);
        if (bool.TryParse(value, out var vb)
            && node is JsonValue jvBool
            && jvBool.TryGetValue<bool>(out var fb))
            return fb == vb;

        var nv = node.ToString();
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var vd)
            && decimal.TryParse(nv, NumberStyles.Any, CultureInfo.InvariantCulture, out var fd))
            return vd == fd;

        return string.Equals(nv, value,
            ci ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool CompareOrdered(JsonNode? node, string value, FilterOperator op)
    {
        if (node is null) return false;
        var s = node.ToString();

        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var nv)
            && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return op switch
            {
                FilterOperator.GreaterThan => nv > v,
                FilterOperator.GreaterThanOrEqual => nv >= v,
                FilterOperator.LessThan => nv < v,
                FilterOperator.LessThanOrEqual => nv <= v,
                _ => false
            };

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var nd)
            && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var vd))
            return op switch
            {
                FilterOperator.GreaterThan => nd > vd,
                FilterOperator.GreaterThanOrEqual => nd >= vd,
                FilterOperator.LessThan => nd < vd,
                FilterOperator.LessThanOrEqual => nd <= vd,
                _ => false
            };

        return false;
    }

    private static IComparable? GetSortKey(JsonObject item, string field)
    {
        var v = GetByPath(item, field);
        if (v is null) return null;
        var s = v.ToString();
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) return dt;
        return s;
    }

    private static int TryParseInt(string? raw, int dflt) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : dflt;

    private static string ToCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsLower(s[0])) return s;
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
