---
title: "PaginatedResponse<T>"
description: Standard pagination wrapper used by GET-list endpoints and consumable from your own handlers.
---

## Paginated response (`PaginatedResponse<T>`)

Generic wrapper for paginated results:

```csharp
// Simple creation
var page = PaginatedResponse<ProductResponse>.Create(items, totalCount: 100, page: 2, pageSize: 10);

// From IQueryable (handles Skip/Take automatically)
var page = await PaginatedResponse<Product>.CreateAsync(_db.Products, page: 1, pageSize: 20);

// Map items (e.g. entity → response DTO)
var response = page.Map(p => ProductResponse.FromEntity(p));

// Properties
page.Items        // IReadOnlyList<T>
page.TotalCount   // int
page.Page         // int
page.PageSize     // int
page.TotalPages   // computed
page.HasNextPage  // computed
page.HasPreviousPage // computed
```

Full example with `[QueryDto]` + `[ResponseDto]`:

```csharp
[HttpGet]
public async Task<IActionResult> List([FromQuery] ProductQuery query, int page = 1, int pageSize = 20)
{
    var filtered = query.Apply(_db.Products);
    var paginated = await PaginatedResponse<Product>.CreateAsync(filtered, page, pageSize);
    var response = paginated.Map(p => ProductResponse.FromEntity(p));
    return Ok(response);
}
```

## Cursor (keyset) pagination (`CursorPage<T>`)

Offset pagination degrades on deep pages (`OFFSET 100000` scans and discards) and can skip/duplicate rows when data changes between pages. Generated minimal-API GET list endpoints also support **keyset pagination** out of the box — pass `cursor=` (empty) to start:

```http
GET /api/products?cursor=&pageSize=20
→ { "items": [...], "nextCursor": "MTIz", "pageSize": 20 }

GET /api/products?cursor=MTIz&pageSize=20
→ { "items": [...], "nextCursor": null, "pageSize": 20 }   // null = last page
```

- Items are ordered by the entity key; the opaque `nextCursor` encodes the last-seen key. The query becomes `WHERE Key > @after ORDER BY Key LIMIT @pageSize` — constant cost at any depth.
- `filter=` composes with cursor mode; `sort=` is ignored (keyset pagination requires the stable key order).
- Supported key types: `int`, `long`, `Guid`, `string`. For other key types the `cursor` parameter is not emitted.
- Without the `cursor` parameter the endpoint behaves exactly as before (offset `PaginatedResponse<T>`).
- Cursor mode returns full `{Entity}Response` items ([ColumnPermission] masking still applies); the separate list-item DTO is an offset-mode feature.

