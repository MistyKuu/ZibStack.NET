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

