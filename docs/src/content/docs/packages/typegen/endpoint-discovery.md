---
title: TypeGen — Endpoint discovery
description: "Three ways TypeGen discovers endpoints for OpenAPI paths and TanStack Query clients — hand-written Minimal API scan, native [ApiController] scan, and [CrudApi] synthesis. All unified, with collision rules."
---

TypeGen populates its endpoint model from three sources, merged into one unified
output. OpenAPI uses it for `paths:`; the TanStack Query emitter uses it for
client functions, keys, hooks, and cache helpers. Hand-written code is always
ground truth — when sources collide on the same (verb, path), the native handler
wins over synthesis.

## Hand-written Minimal API → OpenAPI `paths:`

`app.MapGet("/path", lambda)` and its relatives (`MapPost`/`MapPut`/`MapPatch`/
`MapDelete`) get picked up by a syntactic scan of your source:

```csharp
var app = builder.Build();

app.MapGet("/orders/{id}", (int id, OrderService svc, CancellationToken ct)
    => svc.GetByIdAsync(id, ct));

app.MapPost("/orders", ([FromBody] CreateOrderRequest req, OrderService svc)
    => svc.CreateAsync(req));

app.MapGroup("/api/v1")
    .MapGet("/health", () => Results.Ok());
```

→ three entries in `paths:`, tags from the first path segment, parameters bound
via the same `[FromX]` rules as controllers, return types inferred from the
lambda body.

**What's picked up:**
- **Literal route patterns** (string literals or `const string`-referenced
  constants). Interpolated strings, `string.Concat`, and field reads stay
  unresolvable at compile time and the endpoint is silently skipped.
- **Inline lambdas** (`(x, y) => body`, parenthesized or simple). Method
  references (`app.MapGet("/x", HandlerMethod)`) are resolved when the endpoint
  has an explicit `.WithName(...)` so the generated operation name stays stable.
- **`MapGroup` prefix chains**, including via local variables:
  ```csharp
  var g = app.MapGroup("/api/widgets");
  g.MapGet("/{id}", (int id) => ...);   // emits /api/widgets/{id}
  ```
- **Endpoint names and tags** from `.WithName("operationId")` and
  `.WithTags("Tag")`. These feed OpenAPI `operationId`/`tags` and TanStack
  Query function/key names.
- **Parameter binding**: explicit `[FromRoute]` / `[FromBody]` / `[FromQuery]` /
  `[FromHeader]` first; fallback to ASP.NET convention. `CancellationToken` /
  `HttpContext` / `[FromServices]` params are filtered out. Route placeholders
  without a matching handler parameter are still emitted as required path
  parameters, using common route constraints like `:guid`, `:int`, and
  `:decimal` to infer their type when possible.
- **Return type** unwrapped from `Task<T>` / `ValueTask<T>`. `IResult` yields
  no response schema (untyped success — ASP.NET Core doesn't expose T in that
  path).
- **`.Produces<T>()` response metadata**. When present, TypeGen treats the
  produced type as the explicit response contract. It runs after handler return
  type inference, so `.Produces<T>()` intentionally wins if the two disagree.

**Collisions** follow the same rule as controllers: if Minimal API and
`[CrudApi]` synthesis both claim the same (verb, pattern), the hand-written
`MapX` wins.

**MVP limitations** (track these before relying heavily on the scan):
- Handler delegates passed through fields or other dynamic registrations aren't
  resolved
- `TypedResults.Ok<T>(...)` pattern: response type from the generic arg isn't
  extracted yet (uses the raw `Ok<T>` return type which reads as `IResult`)
- Endpoint filters chained via `.AddEndpointFilter(...)` are ignored (they
  don't change the contract, only runtime behaviour)

## Hand-written controllers → OpenAPI `paths:`

TypeGen scans every `[ApiController]` class (or class inheriting `ControllerBase`)
in your source and contributes its `[HttpGet]` / `[HttpPost]` / `[HttpPut]` /
`[HttpPatch]` / `[HttpDelete]` methods to the emitted OpenAPI document — no
`[CrudApi]` annotation needed, no runtime reflection, just native ASP.NET Core
attributes:

```csharp
[ApiController]
[Route("api/widgets")]
public class WidgetsController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<WidgetResponse> Get(int id) => throw null!;

    [HttpPost]
    public ActionResult<WidgetResponse> Create([FromBody] CreateWidgetRequest req) => throw null!;
}
```

→ emits the matching `paths:` block:

```yaml
paths:
  /api/widgets/{id}:
    get:
      tags: [Widgets]
      operationId: get
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: integer, format: int32 }
      responses:
        '200':
          content: { application/json: { schema: { $ref: '#/components/schemas/WidgetResponse' } } }
  /api/widgets:
    post:
      tags: [Widgets]
      operationId: create
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/CreateWidgetRequest' } } } }
      responses:
        '200':
          content: { application/json: { schema: { $ref: '#/components/schemas/WidgetResponse' } } }
```

**What's picked up:**
- Route template from class-level `[Route("api/[controller]")]` + method-level
  `[HttpX("template")]`, merged segment-wise
- `[controller]` token substitution (strips the `Controller` suffix)
- Parameter binding: explicit `[FromRoute]` / `[FromBody]` / `[FromQuery]` /
  `[FromHeader]` first; fallback to ASP.NET convention (simple types → query or
  route when the name appears in the template, complex types → body)
- Return type unwrapping: `Task<T>` / `ValueTask<T>` / `ActionResult<T>` all
  strip down to `T` for the response schema
- `CancellationToken`, `HttpContext`, `[FromServices]`-annotated params — filtered
  out (infrastructure, not contract)

**Collisions with `[CrudApi]` synthesis:** when a native controller method
claims the same (verb, pattern) that a `[CrudApi]` class would also emit, the
native handler wins. Hand-written controllers are ground truth for what the
API actually exposes — synthesis steps aside.

## `[CrudApi]` → OpenAPI `paths:` (synthesis fallback)

When a class carries `[CrudApi]` (from `ZibStack.NET.Dto`), Dto generates the
endpoints themselves (Minimal API or `[ApiController]` depending on `ApiStyle`).
TypeGen **cannot see** that generated code during the same compilation pass —
Roslyn's cross-generator visibility wall keeps them invisible. So instead of
scanning the generated output, TypeGen **synthesizes** the matching paths
directly from the `[CrudApi]` metadata: it knows what Dto will emit, so it
reconstructs the same paths from the attribute + class shape.

Practically you don't need to care about this split. If you use `[CrudApi]`,
endpoints appear in OpenAPI. If you hand-write an `[ApiController]`, endpoints
appear in OpenAPI. Same `paths:` block, unified code path (see
[Hand-written controllers](#hand-written-controllers--openapi-paths) for the
native scan). When both sources describe the same (verb, path), the native
controller wins — hand-written code is the ground truth.

```csharp
[CrudApi]
[GenerateTypes(Targets = TypeTarget.OpenApi, OutputDir = "generated")]
public partial class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}
```

Emits (via synthesis):

```yaml
paths:
  /api/orders:
    get:    { operationId: listOrder,   tags: [Order], responses: { '200': ... } }
    post:   { operationId: createOrder, tags: [Order], requestBody: { $ref: CreateOrderRequest } }
  /api/orders/{id}:
    get:    { operationId: getOrderById, parameters: [...], responses: { '200': ..., '404': ... } }
    patch:  { operationId: updateOrder,  requestBody: { $ref: UpdateOrderRequest } }
    delete: { operationId: deleteOrder,  responses: { '204': No Content } }
```

What's read from `[CrudApi]`:
- `Route` — explicit override; otherwise convention is `api/{pluralized-class-name-lowercase}`
- `RoutePrefix` — slotted between `api/` and the pluralized class name
- `KeyProperty` — path parameter name (default `Id`); type inferred from the property
- `Operations` — bitmask controlling which verbs emit (default = `GetById | GetList | Create | Update | Delete`)

**What's emitted automatically:**
- GET-list response is a `PaginatedResponseOf{Class}` wrapper (matches the runtime
  `PaginatedResponse<T>` shape: `items`, `totalCount`, `page`, `pageSize`, `totalPages`,
  `hasNextPage`, `hasPreviousPage`). The wrapper schema is added to `components/schemas`.
- `page`/`pageSize` query params on list endpoints; `filter`/`sort`/`select`/`count` as
  well when `ZibStack.NET.Query` is in the compilation (detected by metadata presence).
- Bulk endpoints when flags are set — `POST /{resource}/bulk` (array of requests) and
  `POST /{resource}/bulk-delete` (array of keys).

**What else is emitted automatically (Dto integration):**

When a `[GenerateTypes]` class also carries a Dto attribute (`[CrudApi]`, `[CreateDto]`,
`[UpdateDto]`, `[ResponseDto]`), TypeGen synthesizes the matching companion schemas —
`Create{Class}Request`, `Update{Class}Request`, `{Class}Response` — directly from
the parent's property list, respecting `[DtoIgnore(target)]` / `[DtoOnly(target)]`
filtering. The `$ref`s that `[CrudApi]` paths point at resolve to real schemas, no
annotations beyond `[GenerateTypes]` required.

The filter rules live in `shared/DtoSemantics.cs` — one file linked into both the
Dto and TypeGen generators via `<Compile Include>`, so the two generators can never
drift on what a given `[DtoIgnore(flags)]` means.

**Limitations (MVP):**
- Pluralization is naive `+"s"`. For irregular nouns (`Bus`, `Octopus`, `Person`) use an explicit `Route`.
- `[DtoName]` per-variant custom DTO names aren't read yet — naming is the Dto default convention.
- `[ResponseDto(ListName=...)]` list-item variants aren't synthesized (use the main response schema).
- Authorization policies don't map to `security`/`securitySchemes` yet.
- Hand-written Minimal API endpoints (`app.MapGet("/path", lambda)`) ARE scanned — see the [Minimal API section above](#hand-written-minimal-api--openapi-paths) for what works and what's out of MVP.
