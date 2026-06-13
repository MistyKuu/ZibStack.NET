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

## Naming

For Minimal APIs, prefer `.WithName("searchWorkItems")` and `.WithTags("Workflow")`.
The operation name becomes the function/options/hook base name, and the tag
becomes the query-key group. Without `.WithName(...)`, TypeGen derives a stable
name from verb plus route segments.
