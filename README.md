# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, tracing, DTOs, CRUD APIs, validation, UI metadata, and more. Zero reflection, zero runtime overhead.

**[Documentation](https://mistykuu.github.io/ZibStack.NET/)** | **[Getting Started](https://mistykuu.github.io/ZibStack.NET/getting-started/)** | **[Live Playground](https://zibstack-net.onrender.com/index.html)**

## Three tiers — pick your buy-in

ZibStack is designed so you can adopt as little or as much as you want. Start at Tier 1, move deeper only if it fits your project.

**Tier 1 — Drop-in. Zero architectural buy-in.** Add one attribute, keep everything else unchanged. These work in any .NET 8+ project, solo or team, greenfield or legacy.

- **`[Log]`** — compile-time structured logging with zero boilerplate. Interpolated strings (`LogInformation($"...")`) just work.
- **`[Trace]`** — OpenTelemetry spans on any method, with one attribute. Compatible with Jaeger / Zipkin / OTLP.
- **ZibStack.NET.Aop** — write your own aspects (`IAspectHandler`) for metrics, retry, cache, auth, auditing — a few lines each.

**Tier 2 — Ergonomics. Opt-in per file.** TypeScript-inspired utility types and helpers you reach for when you want them. No framework, no configuration.

- **TypeScript utility types** — `Partial<T>`, `Pick<T, K>`, `Omit<T, K>`, `Intersect<...>` via source generators.
- **`[Destructurable]`** — JS-style object destructuring with rest: `var (name, id, rest) = person.PickNameId()`.
- **`Result<T>`** — functional error handling with `Map`/`Bind`/`Match`.
- **`[ZValidate]`** — compile-time validation from attributes.

**Tier 3 — Opinionated scaffolding. High buy-in, high payoff.** Full-stack CRUD generation, query DSL, UI metadata. Best for solo projects and small teams where the time savings justify the framework buy-in; be cautious on large enterprise codebases where "magic" can surprise teammates.

- **`[CrudApi]` / `[ImTiredOfCrud]`** — one attribute generates DTOs, endpoints, EF/Dapper stores, validation, query DSL, form/table UI schemas.
- **ZibStack.NET.Query** — filter/sort DSL (`filter=Level>25,Team.Name=*ski`) compiled to LINQ/SQL.
- **ZibStack.NET.UI** — compile-time form/table metadata, consumed by any frontend.

> **Try the Playground** — edit C# models with `[ImTiredOfCrud]` to see generated endpoints, DTOs, query DSL, and form/table schemas update in real-time. Hosted on Render's free tier so initial load may be slow — for best experience clone the repo and run locally: `cd packages/ZibStack.NET.UI/sample/SampleApi && dotnet run`

## Why?

**Logging is tedious.** In enterprise systems you need logs everywhere. Wrapping every method in try-catch just for entry/exit logging is boilerplate hell. `[Log]` on a class adds structured logging to every public method — automatic entry, exit, exception, and timing. One attribute, done.

**Structured logging fights you.** `ILogger.LogInformation` requires message templates: `_logger.LogInformation("User {User} bought {Product}", user, product)` — you can't use interpolated strings because they bypass structured logging. With ZibStack.NET.Log, standard `_logger.LogInformation($"User {user}")` just works — a source generator emits compile-time interceptors that dispatch via cached `LoggerMessage.Define<T>` delegates. Zero code changes, ~40x faster when the level is disabled.

**Tracing is boilerplate hell.** Instrumenting a method with OpenTelemetry means wrapping every call in `using var activity = ...` + try/catch + `SetStatus` + tag wiring. Do it once, you've tripled the size of the method. With `[Trace]` it's one attribute, and you get consistent parameter tags, status, elapsed time, and exception reporting for free.

**TypeScript has it, C# doesn't.** `Partial<T>`, `Pick<T, K>`, `Omit<T, K>`, intersection types — if you write frontend code, you miss these in C#. Now you can: `[PartialFrom(typeof(Player))]` generates `PatchField<T>` properties with `ApplyTo()` for patching. `[PickFrom]`, `[OmitFrom]`, `[IntersectFrom]` — all source-generated, strongly-typed.

**JS-style destructuring with rest.** `const { name, id, ...rest } = person` is one of the most missed features when moving from JS/TS to C#. Now: mark a type with `[Destructurable]` and write `var (name, id, rest) = person.PickNameId()` — fully typed, with autocomplete. The source generator scans your `PickXxx()` call sites and emits matching extension methods + 'rest' types on demand (no combinatorial explosion — only the combos you actually use).

**CRUD is 80% copy-paste.** Define a model, write Create/Update/Response DTOs, wire up endpoints, add validation, build query filters, set up EF stores. Or: `[ImTiredOfCrud]` — one attribute generates everything. CRUD API + DTOs + validation + query DSL (filter/sort/select with OR, grouping, IN, dot notation on relations) + form/table UI schemas with `filterOperators` per column. One attribute, full stack.

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides structured interpolated string logging — `LogInformation($"...")` just works. |
| [**ZibStack.NET.Aop**](packages/ZibStack.NET.Aop/) | `dotnet add package ZibStack.NET.Aop` | AOP framework with C# interceptors. Built-in `[Trace]` (OpenTelemetry spans). Custom aspects via `IAspectHandler`/`IAroundAspectHandler`. |
| [**ZibStack.NET.Core**](packages/ZibStack.NET.Core/) | `dotnet add package ZibStack.NET.Core` | Source generator for shared attributes: relationships (`OneToMany`, `OneToOne`, `Entity`), TypeScript-style utility types (`PartialFrom`, `IntersectFrom`, `PickFrom`, `OmitFrom`), JS-style destructuring (`Destructurable` → `PickXxx()` methods). |
| [**ZibStack.NET.Result**](packages/ZibStack.NET.Result/) | `dotnet add package ZibStack.NET.Result` | Functional Result monad (`Result<T>`) with Map/Bind/Match, error handling without exceptions. |
| [**ZibStack.NET.Validation**](packages/ZibStack.NET.Validation/) | `dotnet add package ZibStack.NET.Validation` | Source generator for compile-time validation from attributes (`[ZRequired]`, `[ZEmail]`, `[ZRange]`, `[ZMatch]`). |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for CRUD DTOs (Create/Update/Response/Query) with PatchField support and full CRUD API generation. |
| [**ZibStack.NET.Query**](packages/ZibStack.NET.Query/) | `dotnet add package ZibStack.NET.Query` | Filter/sort DSL for REST APIs. Parses query strings (`filter=Level>25,Team.Name=*ski&sort=-Level`) into LINQ/SQL. Compile-time field allowlists via source generation. |
| [**ZibStack.NET.EntityFramework**](packages/ZibStack.NET.EntityFramework/) | `dotnet add package ZibStack.NET.EntityFramework` | EF Core integration for Dto CRUD API. Auto-generates stores + DI registration from `DbContext`. |
| [**ZibStack.NET.Dapper**](packages/ZibStack.NET.Dapper/) | `dotnet add package ZibStack.NET.Dapper` | Dapper integration for Dto CRUD API. `DapperCrudStore` base class with auto-generated SQL. |
| [**ZibStack.NET.UI**](packages/ZibStack.NET.UI/) | `dotnet add package ZibStack.NET.UI` | Source generator for UI form/table metadata. Annotate models, get compile-time form descriptors and table column definitions. |

---

## Tier 1 — Drop-in

### ZibStack.NET.Log

```csharp
// On a method:
[Log]
public Order PlaceOrder(int customerId, [Sensitive] string creditCard) { ... }
// log: Entering OrderService.PlaceOrder(customerId: 42, creditCard: ***)
// log: Exited OrderService.PlaceOrder in 53ms -> {"Id":1,"Product":"Widget"}

// On a class — logs ALL public methods:
[Log]
public class OrderService { ... }

// Interpolated string logging — works out of the box, no using needed:
logger.LogInformation($"User {userId} bought {product} for {total:C}");
// Source generator emits compile-time interceptor:
// → cached LoggerMessage.Define<int, string, decimal> with literal template
// → ~40x faster when level is disabled (one IsEnabled check)

// Optional: override assembly-level defaults (default: Information level, Destructure mode)
[assembly: ZibLogDefaults(EntryExitLevel = ZibLogLevel.Debug, ObjectLogging = ObjectLogMode.Json)]
```

### ZibStack.NET.Aop — built-in `[Trace]` + custom aspects

```csharp
// Built-in: OpenTelemetry-compatible tracing — one attribute, span per call:
[Trace]
public async Task<Order> GetOrderAsync(int id) { ... }
// → Jaeger / Zipkin / OTLP see: OrderService.GetOrderAsync
//   with parameters as tags, elapsed_ms, status, exception info — all automatic

// Customize the source name, operation name, or skip parameter tags:
[Trace(SourceName = "checkout.orders", IncludeParameters = false)]
public Task PlaceOrderAsync(Order order) { ... }

// Stack multiple aspects — all run in a single generated interceptor:
[Log]
[Trace]
public async Task<Order> ProcessOrderAsync(int id) { ... }
```

Write your own aspects — just a class + attribute:

```csharp
[AspectHandler(typeof(TimingHandler))]
public class TimingAttribute : AspectAttribute { }

public class TimingHandler : IAspectHandler
{
    public void OnBefore(AspectContext ctx)
        => Console.WriteLine($"Starting {ctx.MethodName}({ctx.FormatParameters()})");
    public void OnAfter(AspectContext ctx)
        => Console.WriteLine($"Completed {ctx.MethodName} in {ctx.ElapsedMilliseconds}ms");
    public void OnException(AspectContext ctx, Exception ex)
        => Console.WriteLine($"Failed {ctx.MethodName}: {ex.Message}");
}

// Apply it:
[Timing]
public Order GetOrder(int id) { ... }
```

Setup — one-liner for built-ins, standard DI for your own:

```csharp
builder.Services.AddAop();           // built-in handlers ([Trace], ...)
builder.Services.AddTransient<TimingHandler>();

var app = builder.Build();
app.Services.UseAop();                        // bridges DI into the aspect runtime
```

Async handlers (for async methods only):

```csharp
public class MetricsHandler : IAsyncAspectHandler
{
    public ValueTask OnBeforeAsync(AspectContext ctx) => default;
    public async ValueTask OnAfterAsync(AspectContext ctx)
        => await _client.RecordAsync(ctx.MethodName, ctx.ElapsedMilliseconds);
    public ValueTask OnExceptionAsync(AspectContext ctx, Exception ex) => default;
}
```

---

## Tier 2 — Ergonomics

### ZibStack.NET.Core — `[Destructurable]`

```csharp
[Destructurable]
public partial class Person
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
    public string Email { get; set; }  = "";
    public int Age { get; set; }
    public string City { get; set; } = "";
}

// JS-style destructuring — fully typed, with autocomplete:
var (name, rest) = person.PickName();             // rest: PersonRest_Name { Id, Email, Age, City }
var (name, id, rest) = person.PickNameId();       // rest: PersonRest_NameId { Email, Age, City }
var (name, id, email, rest) = person.PickNameIdEmail();

// Generator scans every PickXxx() call site and emits matching extension methods
// + 'rest' types on demand. No combinatorial explosion — only the combos you use.
// Hover Person in the IDE → see all generated picks via <see cref> XML links.
```

### ZibStack.NET.Result

```csharp
public Result<Order> GetOrder(int id)
{
    if (id <= 0) return Result<Order>.Failure(Error.Validation("Invalid ID"));
    var order = _repo.Find(id);
    return order is null ? Result<Order>.Failure(Error.NotFound("Order not found"))
                         : Result<Order>.Success(order);
}

// Usage with Map/Bind/Match:
var result = GetOrder(42)
    .Map(o => o.Total)
    .Match(
        onSuccess: total => $"Total: {total}",
        onFailure: error => $"Error: {error.Message}");
```

### ZibStack.NET.Validation

```csharp
[ZValidate]
public partial class CreateUserRequest
{
    [ZRequired] [ZMinLength(2)] public string Name { get; set; } = "";
    [ZRequired] [ZEmail]        public string Email { get; set; } = "";
    [ZRange(18, 120)]          public int Age { get; set; }
    [ZMatch(@"^\+?\d{7,15}$")] public string? Phone { get; set; }
}

// Generated Validate() method:
var result = request.Validate();
if (!result.IsValid) return BadRequest(result.Errors);
```

---

## Tier 3 — Opinionated scaffolding

> **Before you adopt Tier 3:** these generators move a lot of code out of your hands. On solo or small-team projects the time savings are massive. On larger teams where everyone needs to understand the generated code, start with Tier 1 — adopt Tier 3 only when the whole team has seen how it works. The [Playground](https://zibstack-net.onrender.com/index.html) is the fastest way to show teammates what's generated.

### ZibStack.NET.Dto

```csharp
// One attribute = full CRUD API with auto-generated DTOs + endpoints:
[CrudApi]
public class Player
{
    [DtoIgnore]  public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }

    [CreateOnly]     public required string Password { get; set; }
    [ResponseIgnore] public DateTime CreatedAt { get; set; }
}

// EF Core — auto-generated stores from DbContext:
[GenerateCrudStores]
public class AppDbContext : DbContext
{
    public DbSet<Player> Players => Set<Player>();
}

// Program.cs — three lines:
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
builder.Services.AddAppDbContextCrudStores();   // auto-generated DI registration
app.MapPlayerEndpoints();                        // auto-generated GET/POST/PATCH/DELETE
```

### ZibStack.NET.Query

```csharp
// Add ZibStack.NET.Query to your project — the Dto generator auto-detects it
// and adds filter/sort string params to all CRUD list endpoints:

GET /api/players?filter=Level>25,Name=*ski&sort=-Level&page=1&pageSize=20
GET /api/players?filter=Team.Name=Lakers                    // relation → auto JOIN
GET /api/players?filter=(Level>50|Level<10),Team.City=LA    // OR + grouping
GET /api/players?filter=Name=in=Jan;Anna;Kasia              // IN list
GET /api/players?filter=Email=*@test.pl/i&sort=Team.Name    // case insensitive + relation sort
GET /api/teams?filter=Players.Name=*ski                     // OneToMany → filter by child properties
GET /api/players?filter=Level>25&count=true                 // count only → { "count": 42 }
GET /api/players?select=Name,Level,Team.Name                // field selection

// [ZQuery] — standalone query DSL attribute (same as [QueryDto] with Sortable=true default)
// Operators: = != > >= < <= =* !* ^ !^ $ !$ =in= =out=
// Logic: , (AND) | (OR) () (grouping) /i (case insensitive)
```

### ZibStack.NET.UI

```csharp
// Annotate models for forms + tables — generates JSON metadata at compile time:
[UiForm]
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/player")]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
public partial class PlayerView
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [ZRequired] [ZMinLength(2)]
    [UiFormField(Label = "Name", Placeholder = "Enter name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Select(typeof(Region))]
    [UiFormField(Label = "Region", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public Region Region { get; set; }

    [Slider(Min = 1, Max = 100)]
    [UiFormField(Label = "Level", Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public int Level { get; set; }
}

// Generated: PlayerViewFormDescriptor, PlayerViewTableDescriptor, PlayerViewJsonSchema
// → Consume from Blazor, React, Vue, Angular — framework-agnostic JSON metadata
```

### `[ImTiredOfCrud]` — one attribute, full-stack CRUD

The capstone. One attribute on your model generates: CRUD API + DTOs + validation + form/table UI schemas + Query DSL (filter, sort, select, pagination). The frontend reads the JSON schemas and renders everything automatically — zero configuration.

![ImTiredOfCrud Demo](docs/src/assets/imtiredofcrud-demo.png)

## Repository Structure

```
ZibStack.NET/
├── packages/
│   ├── ZibStack.NET.Aop/              → AOP framework (aspects, interceptors, [Trace])
│   ├── ZibStack.NET.Log/              → Logging source generator
│   ├── ZibStack.NET.Core/             → Shared attributes (relations, utility types)
│   ├── ZibStack.NET.Dto/              → DTO + CRUD API source generator
│   ├── ZibStack.NET.Query/            → Filter/sort DSL for REST APIs
│   ├── ZibStack.NET.EntityFramework/  → EF Core integration for Dto CRUD
│   ├── ZibStack.NET.Dapper/           → Dapper integration for Dto CRUD
│   ├── ZibStack.NET.Result/           → Result monad (Map/Bind/Match)
│   ├── ZibStack.NET.Validation/       → Validation source generator
│   └── ZibStack.NET.UI/               → UI form/table metadata generator
├── .github/workflows/
│   ├── ci.yml                         → Builds & tests all packages
│   ├── release-all.yml                → Release all packages to NuGet
│   └── release-{package}.yml          → Individual package releases
└── ZibStack.NET.slnx
```

## License

MIT
