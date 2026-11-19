---
title: TypeGen - TanStack Query emitter
description: "TypeTarget.TanStackQuery emits typed TanStack Query clients from discovered ASP.NET endpoints: fetch functions, query keys, options factories, hooks, and cache helpers."
---

`TypeTarget.TanStackQuery` emits TypeScript client code for
`@tanstack/react-query` v5 from the same endpoint model used by TypeGen's
OpenAPI paths. It scans:

- hand-written Minimal APIs (`MapGet`, `MapPost`, `MapGroup`, `.WithName(...)`, `.WithTags(...)`)
- hand-written `[ApiController]` actions
- `[CrudApi]` synthesis from `ZibStack.NET.Dto`

Use it with `TypeTarget.TypeScript` so request and response model files are
generated in the same build.

## Install in the frontend

```bash
npm install @tanstack/react-query
```

## Minimal API setup

```csharp
using Microsoft.AspNetCore.Mvc;
using ZibStack.NET.Dto;
using ZibStack.NET.TypeGen;

var workflow = app.MapGroup("/api/workflow").WithTags("Workflow");

workflow.MapGet("/workspaces/{workspaceId:guid}/items",
        (Guid workspaceId,
         [FromQuery] string? search,
         [FromQuery] WorkItemState? state,
         [FromQuery] string[]? labels,
         [FromQuery] int page = 1,
         [FromQuery] int pageSize = 20) =>
            PaginatedResponse<WorkItemSummary>.Create(items, items.Count, page, pageSize))
    .WithName("searchWorkItems")
    .WithTags("Workflow");

workflow.MapPost("/workspaces/{workspaceId:guid}/items",
        (Guid workspaceId, [FromBody] CreateWorkItemCommand body) => CreateItem(body))
    .WithName("createWorkItem")
    .WithTags("Workflow");
```

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript
                       | TypeTarget.OpenApi
                       | TypeTarget.TanStackQuery,
               OutputDir = "../client/src/api")]
public class WorkItemSummary
{
    public Guid Id { get; set; }
    public required string Title { get; set; } = "";
    public WorkItemState State { get; set; }
    public List<string> Labels { get; set; } = new();
}

[GenerateTypes(Targets = TypeTarget.TypeScript
                       | TypeTarget.OpenApi
                       | TypeTarget.TanStackQuery,
               OutputDir = "../client/src/api")]
public class CreateWorkItemCommand
{
    public required string Title { get; set; } = "";
    public WorkItemState InitialState { get; set; }
}
```

## Configuration

```csharp
public sealed class TypeGenConfig : ITypeGenConfigurator
{
    public void Configure(ITypeGenBuilder b)
    {
        b.TypeScript(ts =>
        {
            ts.OutputDir = "../client/src/api";
            ts.PropertyNameStyle = NameStyle.CamelCase;
        });

        b.TanStackQuery(q =>
        {
            q.OutputDir = "../client/src/api";
            q.SingleFileName = "api.gen.ts";
            q.BaseUrlExpression = "import.meta.env.VITE_API_URL";
            // q.PreserveBaseUrlPath = true; // reverse-proxy service prefix
            // q.FileLayout = QueryFileLayout.SplitByTag;
            // q.ApiClientImportPath = "./http-client";
            // q.ApiClientName = "request";
        });
    }
}
```

The default fetch client evaluates `BaseUrlExpression` defensively. If the
configured expression is missing, empty, or throws, it falls back to
`window.location.origin` in browser environments, then to `http://localhost` for
non-browser execution. You can still set `BaseUrlExpression` to a literal origin
or import a custom client when you want stricter environment handling.

### Reverse-proxy service prefixes

Set `PreserveBaseUrlPath = true` when the configured base URL includes the
service's externally visible prefix:

```csharp
b.TanStackQuery(q =>
{
    q.BaseUrlExpression = "import.meta.env.VITE_API_URL";
    q.PreserveBaseUrlPath = true;
});
```

For example, set `VITE_API_URL=http://localhost:3100/aoi`. The built-in client
then resolves `/asdf` to `http://localhost:3100/aoi/asdf`. No custom client or
generated-file edits are needed.

The default is `false` to preserve existing consumers' standard URL resolution.
In that mode, `/asdf` resolves from the origin and a base's final pathname
segment without a trailing slash is treated as a file:

| Base URL | Route | Default (`false`) | Preserve path (`true`) |
|---|---|---|---|
| `https://host/aoi` | `/asdf` | `https://host/asdf` | `https://host/aoi/asdf` |
| `https://host/aoi` | `asdf` | `https://host/asdf` | `https://host/aoi/asdf` |
| `https://host/proxy/aoi/` | `/asdf` | `https://host/asdf` | `https://host/proxy/aoi/asdf` |
| `https://host` | `/asdf` | `https://host/asdf` | `https://host/asdf` |

With preservation enabled, the base pathname is treated as a directory, with
or without a trailing slash. A root (`/`) or empty route resolves to that
directory. Endpoint routes should contain the backend route only; including
the proxy prefix in both the base and route duplicates it.

Absolute URLs (`https://other/path`) and protocol-relative paths (`//other/path`)
retain standard URL semantics in both modes: they can select another host and
bypass the service prefix. Dot segments still follow URL normalization rules;
this setting is not a restriction on where requests may be sent.

Route encoding and query handling remain unchanged. Encoded route values stay
encoded, route query strings are retained, and `options.query` sets scalar
values or appends array values using `URLSearchParams`. Base query strings and
fragments follow standard URL rules (an empty route inherits the base query).
The existing base URL fallback still applies. `ApiClientImportPath` continues
to replace the built-in client entirely; this option does not alter imported
clients.

## Generated shape

```typescript
import { mutationOptions, queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import type { CreateWorkItemCommand } from './CreateWorkItemCommand';
import type { WorkItemState } from './WorkItemState';
import type { WorkItemSummary } from './WorkItemSummary';

export type PaginatedResponseOfWorkItemSummary = {
    items: WorkItemSummary[];
    totalCount: number;
    page: number;
    pageSize: number;
    totalPages: number;
    hasNextPage: boolean;
    hasPreviousPage: boolean;
};

export const workflowKeys = {
    all: ['workflow'] as const,
    searchWorkItems: (input: SearchWorkItemsInput) => [...workflowKeys.all, 'searchWorkItems', input] as const,
};

export type SearchWorkItemsInput = {
    workspaceId: string;
    search?: string;
    state?: WorkItemState;
    labels?: string[];
    page?: number;
    pageSize?: number;
};

export function searchWorkItems(input: SearchWorkItemsInput, signal?: AbortSignal): Promise<PaginatedResponseOfWorkItemSummary> {
    return apiFetch<PaginatedResponseOfWorkItemSummary>(`/api/workflow/workspaces/${encodeURIComponent(String(input.workspaceId))}/items`, {
        method: 'GET',
        query: {
            search: input.search,
            state: input.state,
            labels: input.labels,
            page: input.page,
            pageSize: input.pageSize,
        },
        signal,
    });
}

export function searchWorkItemsOptions(input: SearchWorkItemsInput) {
    return queryOptions({
        queryKey: workflowKeys.searchWorkItems(input),
        queryFn: ({ signal }) => searchWorkItems(input, signal),
    });
}

export function useSearchWorkItems(input: SearchWorkItemsInput) {
    return useQuery(searchWorkItemsOptions(input));
}
```

Mutations get a fetch function, `mutationOptions`, a React hook, and tag-wide
invalidation:

```typescript
export type CreateWorkItemInput = {
    workspaceId: string;
    body: CreateWorkItemCommand;
};

export function createWorkItem(input: CreateWorkItemInput, signal?: AbortSignal): Promise<WorkItemSummary> {
    return apiFetch<WorkItemSummary>(`/api/workflow/workspaces/${encodeURIComponent(String(input.workspaceId))}/items`, {
        method: 'POST',
        body: input.body,
        signal,
    });
}

export function useCreateWorkItem() {
    const queryClient = useQueryClient();
    return useMutation({
        ...createWorkItemMutationOptions(),
        onSuccess: async () => {
            await invalidateWorkflowQueries(queryClient);
        },
    });
}

export function invalidateWorkflowQueries(queryClient: QueryClient) {
    return queryClient.invalidateQueries({ queryKey: workflowKeys.all });
}
```

## Output settings

| Setting | Default | Purpose |
|---|---|---|
| `OutputDir` | TypeScript output dir, then first model output dir | Where query files are written |
| `FileLayout` | `QueryFileLayout.SingleFile` | `SingleFile` or `SplitByTag` |
| `SingleFileName` | `api.gen.ts` | File name for single-file mode |
| `BaseUrlExpression` | `import.meta.env.VITE_API_URL` | Base URL expression used by the default fetch client; falls back to `window.location.origin` when unset |
| `ApiClientImportPath` | `null` | Import a custom client instead of emitting `apiFetch` |
| `ApiClientName` | `apiFetch` | Default or imported client function name |
| `ModelsImportPath` | computed | Force model type imports from one module |
| `SchemasImportPath` | computed | Force generated Zod schema imports from one module |
| `PayloadValidation` | `None` | `None`, `Responses`, or `RequestsAndResponses` runtime parsing |
| `EmitQueryOptions` | `true` | Emit `queryOptions(...)` helpers |
| `EmitMutationOptions` | `true` | Emit `mutationOptions(...)` helpers |
| `EmitHooks` | `true` | Emit `useQuery` / `useMutation` wrappers |
| `EmitCacheHelpers` | `true` | Emit invalidation and prefetch helpers |

## Custom fetch client

Set `ApiClientImportPath` when your app already has auth, retry, tenant, or
observability behavior in one HTTP client:

```csharp
b.TanStackQuery(q =>
{
    q.ApiClientImportPath = "@/lib/api-client";
    q.ApiClientName = "request";
});
```

The imported function is called like this:

```typescript
request<T>(path, {
    method,
    query,
    headers,
    body,
    signal,
});
```

`query` values can be scalar or arrays. The generated default client appends
arrays as repeated query-string keys and JSON-serializes request bodies.
Route and query parameter types use the same primitive mapping as generated
models; notably `decimal` maps to `string` to preserve precision.

## Runtime payload validation

Add `TypeTarget.Zod` to the request/response DTOs, install `zod@^4.6.1`, and opt in:

```csharp
b.TanStackQuery(q =>
{
    q.PayloadValidation = QueryPayloadValidation.RequestsAndResponses;
    // Optional when TypeGen can compute the relative schema paths:
    q.SchemasImportPath = "../validation/schemas";
});
```

`Responses` parses successful API payloads before returning them to TanStack
Query. `RequestsAndResponses` additionally parses JSON request bodies before
they are sent. `None` preserves the existing zero-Zod-dependency client.

## Naming

For Minimal APIs, prefer `.WithName("searchWorkItems")` and `.WithTags("Workflow")`.
The operation name becomes the function/options/hook base name, and the tag
becomes the query-key group. Without `.WithName(...)`, TypeGen derives a stable
name from verb plus route segments.
