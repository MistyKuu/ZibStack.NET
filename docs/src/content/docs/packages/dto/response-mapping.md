---
title: Response DTOs, mapping, ApplyWithChanges
description: Response DTO generation, FromEntity / ProjectFrom, nested + flatten mapping, set-once fields, Diff(), DtoMapper, Swagger plumbing.
---

## `ApplyWithChanges()` (Update DTOs only)

Like `ApplyTo()` but returns a tuple with the list of actually changed field names. Available on Update, Combined, and UpdateDtoFor requests:

```csharp
var (changedFields, entity) = request.ApplyWithChanges(existingProduct);
// changedFields: ["price", "stock"]
// Useful for audit logs, webhooks, selective cache invalidation
```

### Response DTO (`[ResponseDto]`)

Generates a read-only record for GET responses with `FromEntity()` and IQueryable `ProjectFrom()`:

```csharp
[CreateDto]
[UpdateDto]
[ResponseDto]
public class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    
    [DtoIgnore(DtoTarget.Response)]
    public required string Password { get; set; }
}
```

```csharp
// Generated — plain properties, no PatchField
public record PlayerResponse
{
    public int Id { get; init; }       // DtoIgnore(DtoTarget.Create|Update|Query) doesn't affect Response
    public string Name { get; init; }
    // Password excluded by [DtoIgnore(DtoTarget.Response)]
    
    public static PlayerResponse FromEntity(Player entity) => ...;
    public static IQueryable<PlayerResponse> ProjectFrom(IQueryable<Player> query) => ...;
}

// Usage
[HttpGet("{id}")]
public IActionResult Get(int id)
{
    var player = _db.Players.Find(id);
    return Ok(PlayerResponse.FromEntity(player));
}

// EF Core projection — only fetches needed columns
[HttpGet]
public IActionResult List()
{
    var responses = PlayerResponse.ProjectFrom(_db.Players).ToList();
    return Ok(responses);
}
```

### Auto-recursive nested DTOs

When a model has `[CreateDto]` or `[UpdateDto]`, nested complex type properties automatically get their own DTOs generated — **no need to annotate nested types**. This works recursively to any depth, with deduplication (if a nested type already has an explicit `[UpdateDto]`, its DTO is reused).

#### 3-level example — Employee → Company → ContactInfo

```csharp
// Your models — only the top level has attributes:
[CreateDto]
[UpdateDto]
public class Employee
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)] public int Id { get; set; }
    public required string Name { get; set; }
    public Company? Company { get; set; }          // Level 2 — auto-generated
}

public class Company
{
    public required string Name { get; set; }
    public ContactInfo? Contact { get; set; }      // Level 3 — auto-generated
}

public class ContactInfo
{
    public required string Phone { get; set; }
    public string? Fax { get; set; }
}
```

The generator produces **three** Update request records from a single `[UpdateDto]`:

```csharp
// Generated — Level 1
public record UpdateEmployeeRequest : ICanApply<Employee>, ICanValidate
{
    public PatchField<string> Name { get; init; }
    public PatchField<UpdateCompanyRequest?> Company { get; init; }   // nested DTO, not Company

    public void ApplyTo(Employee target)
    {
        if (Name.HasValue) target.Name = Name.Value!;
        if (Company.HasValue)
        {
            if (Company.Value is null)
                target.Company = null;                                 // explicit clear
            else if (target.Company is not null)
                Company.Value.ApplyTo(target.Company);                 // recursive partial update
        }
    }
}

// Generated — Level 2 (auto, no attribute on Company)
public record UpdateCompanyRequest : ICanApply<Company>, ICanValidate
{
    public PatchField<string> Name { get; init; }
    public PatchField<UpdateContactInfoRequest?> Contact { get; init; }

    public void ApplyTo(Company target)
    {
        if (Name.HasValue) target.Name = Name.Value!;
        if (Contact.HasValue)
        {
            if (Contact.Value is null)
                target.Contact = null;
            else if (target.Contact is not null)
                Contact.Value.ApplyTo(target.Contact);                 // chain continues
        }
    }
}

// Generated — Level 3 (auto, leaf)
public record UpdateContactInfoRequest : ICanApply<ContactInfo>, ICanValidate
{
    public PatchField<string> Phone { get; init; }
    public PatchField<string?> Fax { get; init; }

    public void ApplyTo(ContactInfo target)
    {
        if (Phone.HasValue) target.Phone = Phone.Value!;
        if (Fax.HasValue) target.Fax = Fax.Value;
    }
}
```

Now a 3-level-deep partial update is a single PATCH:

```json
PATCH /api/employees/1
{
  "company": {
    "contact": {
      "fax": null
    }
  }
}
```

Only `employee.Company.Contact.Fax` is cleared. `Company.Name`, `Contact.Phone`, `Employee.Name` — all untouched. Each level's `ApplyTo` checks `HasValue` independently, so the partial-update semantics compose naturally without any manual wiring.

#### Create DTOs — same recursive pattern

`[CreateDto]` follows the same structure, but with `ToEntity()` that chains construction:

```csharp
// Generated
public record CreateEmployeeRequest : ICanCreate<Employee>, ICanValidate
{
    public PatchField<string> Name { get; init; }
    public PatchField<CreateCompanyRequest?> Company { get; init; }

    public Employee ToEntity()
    {
        return new Employee
        {
            Name = Name.HasValue ? Name.Value! : default!,
            Company = Company.HasValue && Company.Value is not null
                ? Company.Value.ToEntity()     // recursive construction
                : default,
        };
    }
}
```

#### Key patterns in the generated code

1. **`PatchField<UpdateXxxRequest?>` wrapping** — the nested type in the parent DTO is the *generated request*, not the original entity. This is what makes tri-state tracking recursive: `Company.HasValue == false` means "don't touch Company at all", `Company.Value == null` means "clear Company", `Company.Value != null` means "apply partial changes to Company's fields".

2. **Null-safe `ApplyTo` chaining** — the generator emits `if (target.Company is not null)` before calling the nested `ApplyTo`. If the parent's navigation is null and the client sends a partial update to it, the update is silently skipped (you can't `ApplyTo` a null target). To create a new nested object from a PATCH, the client should use a full object value, not a partial one.

3. **Deduplication** — if `ContactInfo` is used in multiple parent types (`Employee.Company.Contact` and `Project.Lead`), the generator emits `UpdateContactInfoRequest` once and reuses it everywhere.

4. **`ProjectFrom()` skips nested properties** — in the Response DTO, `ProjectFrom()` (LINQ-to-SQL projection) does not project nested objects because EF Core requires `.Include()` for navigation properties. Use `FromEntity()` with `.Include()` for nested responses.

### Nested type mapping in Response

When a property's type also has `[ResponseDto]`, the generator uses the nested response DTO and maps via `FromEntity()` with null checks:

```csharp
// Generated
public record OrderResponse
{
    public int Id { get; init; }
    public string Title { get; init; }
    public OrderLineResponse? Line { get; init; }

    public static OrderResponse FromEntity(Order entity)
    {
        return new OrderResponse
        {
            Id = entity.Id,
            Title = entity.Title,
            Line = entity.Line is not null
                ? OrderLineResponse.FromEntity(entity.Line)    // null-safe nested mapping
                : null,
        };
    }

    public static IQueryable<OrderResponse> ProjectFrom(IQueryable<Order> query)
    {
        return query.Select(x => new OrderResponse
        {
            Id = x.Id,
            Title = x.Title,
            // Line is NOT projected — use FromEntity() with .Include(x => x.Line) instead
        });
    }
}
```

`ProjectFrom()` is EF Core-safe (no navigation property access in the LINQ expression). For nested data, load via `Include` and map with `FromEntity`:

```csharp
var order = await db.Orders.Include(o => o.Line).FirstAsync(o => o.Id == id);
return OrderResponse.FromEntity(order);   // nested Line is mapped via OrderLineResponse.FromEntity
```

### Flatten nested properties (`[Flatten]`)

Collapses nested object properties into the parent DTO:

```csharp
[ResponseDto]
public class Store
{
    public string Name { get; set; }
    
    [Flatten]
    public Address? Location { get; set; }
}
// Generated StoreResponse has: LocationStreet, LocationCity, LocationZipCode
// FromEntity maps: entity.Location?.Street → LocationStreet
```

### Validation attribute propagation

Attributes from `System.ComponentModel.DataAnnotations` are automatically copied to generated DTOs:

```csharp
public class User
{
    [ZMaxLength(100)]
    [ZEmail]
    public required string Email { get; set; }

    [ZRange(1, 999)]
    public int Quantity { get; set; }
}
// Generated CreateUserRequest.Email has [ZMaxLength(100)] and [ZEmail]
```

### Set-once (immutable) fields

> **Migration note:** The `[Immutable]` attribute has been removed. Use `[DtoIgnore(DtoTarget.Update)]` instead — the property won't appear in the PATCH DTO at all, which is cleaner than silently ignoring changes.

```csharp
[CreateDto]
[UpdateDto]
public class Article
{
    public required string Title { get; set; }
    
    [DtoIgnore(DtoTarget.Update)]
    public required string Slug { get; set; }  // set at creation, never changed
}
```

### `Diff(T entity)` method

Update DTOs include `Diff()` — compares request with an entity and returns changed field names:

```csharp
var changes = request.Diff(existingProduct);
// ["price", "stock"] — useful for audit logs

if (changes.Count == 0) return NoContent(); // nothing actually changed
```

### `DtoMapper`

Generic runtime mapper for copying properties between objects by matching names:

```csharp
var copy = DtoMapper.Map<Product, ProductDto>(product);
DtoMapper.MapTo(source, target);
```

### Swagger / OpenAPI support

When `Swashbuckle.AspNetCore` is detected at compile time, the generator **automatically** emits a `PatchFieldSchemaFilter` that unwraps `PatchField<T>` to its inner type in the Swagger schema — no manual registration needed. Just install the package:

```bash
dotnet add package Swashbuckle.AspNetCore
```

Without Swashbuckle, `PatchField<T>` shows as `{ "hasValue": true, "value": "Bob" }` in the OpenAPI schema. With it, the schema filter collapses it to just `"Bob"` (or `null | "Bob"` for nullable types).

> Both Swashbuckle legacy (pre-v10) and v10+ with `IOpenApiSchema` are supported — the generator detects the API surface at compile time and emits the correct filter variant.

