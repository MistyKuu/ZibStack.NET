---
title: PatchField Tri-State — Null vs Missing
description: How PatchField<T> distinguishes "field not set", "field set to null", and "field set to a value" in PATCH request DTOs, and how to pattern-match on all three in handlers.
---

Every PATCH API eventually hits this problem: a client sends a JSON body, and the server has to figure out which fields the client **meant to change** and which ones it **meant to leave alone**. Two nearly-identical HTTP requests have completely different semantics, and most DTO shapes can't tell them apart.

This guide is about the shape that can: **`PatchField<T>`**, the tri-state wrapper that `ZibStack.NET.Dto` generates for every `[UpdateDto]` / `[CrudApi]` class. After reading this you'll know the problem it solves, why nullable properties alone can't solve it, and how to pattern-match on all three states in your handler code.

## The problem

Two PATCH requests to the same endpoint:

```bash
# Request A
curl -X PATCH /api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"level":99}'

# Request B
curl -X PATCH /api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"level":99,"email":null}'
```

What should happen?

- **Request A** — "change the level to 99". The email is **not mentioned**, so it should stay whatever it was. If the player had `alice@test.com`, they still have `alice@test.com`.
- **Request B** — "change the level to 99 **and clear the email**". The email is mentioned, and it's mentioned as `null`, which is a positive assertion: "this field should now be null".

Two different intents, two different database updates:

```sql
-- A
UPDATE Players SET Level = 99 WHERE Id = 1;

-- B
UPDATE Players SET Level = 99, Email = NULL WHERE Id = 1;
```

## Why nullable properties fail

The natural C# attempt is a nullable DTO:

```csharp
public class UpdatePlayerRequest
{
    public int? Level { get; set; }
    public string? Email { get; set; }
}
```

Let's deserialize both requests into this shape:

```csharp
// Request A body:  {"level":99}
var a = JsonSerializer.Deserialize<UpdatePlayerRequest>(/* … */);
// a.Level = 99
// a.Email = null

// Request B body:  {"level":99,"email":null}
var b = JsonSerializer.Deserialize<UpdatePlayerRequest>(/* … */);
// b.Level = 99
// b.Email = null
```

**The two requests produce identical DTO instances.** There is no way to tell them apart looking at the deserialized object. The nullable property can only hold two states — "has value `X`" or "is null" — but we need **three** states: not sent, sent as null, sent as value.

Most .NET APIs sidestep this by pretending the problem doesn't exist and using `PUT` with full-body replacement instead (client must resend every field). This works until:

- Your DTO has 50 fields and you don't want mobile clients to ship the full object on every edit
- Two clients edit the same record concurrently — PUT-with-full-body silently overwrites each other's changes
- You need offline-first semantics where clients accumulate deltas

At that point you need real partial updates, and nullable properties won't cut it.

## The solution: a tri-state wrapper

`PatchField<T>` is a `readonly struct` with two pieces of state: a value, and a "was this set?" flag.

```csharp
// From ZibStack.NET.Dto (generated per project):
public readonly struct PatchField<T>
{
    private readonly T _value;
    private readonly bool _hasValue;

    public bool HasValue => _hasValue;
    public T Value => _value;

    public PatchField(T value) { _value = value; _hasValue = true; }
    public static implicit operator PatchField<T>(T value) => new(value);
}
```

Three possible states for any `PatchField<string?>` field:

| State | `HasValue` | `Value` | Meaning |
|---|---|---|---|
| Not set | `false` | default (null for ref types) | Client did not mention this field — leave it alone |
| Set to null | `true` | `null` | Client explicitly asked to clear the field |
| Set to value | `true` | e.g. `"alice@test.com"` | Client asked to write a new value |

The custom `PatchFieldJsonConverterFactory` (shipped by `ZibStack.NET.Dto`) reads this distinction straight from the JSON token stream: if the property isn't in the JSON object at all, `HasValue` stays `false`. If the property is present with any value including `null`, `HasValue` is `true`.

## Using `PatchField<T>` in practice

`[CrudApi]` and `[UpdateDto]` both auto-generate an update DTO with `PatchField<T>` for every property. You don't usually write these by hand:

```csharp
[CrudApi]
public partial class Player
{
    [DtoIgnore] public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }
}

// Generated:
public partial record UpdatePlayerRequest
{
    public PatchField<string>  Name  { get; init; }
    public PatchField<int>     Level { get; init; }
    public PatchField<string?> Email { get; init; }

    public void ApplyTo(Player target)
    {
        if (Name.HasValue)  target.Name  = Name.Value;
        if (Level.HasValue) target.Level = Level.Value;
        if (Email.HasValue) target.Email = Email.Value;
    }
}
```

`ApplyTo` walks each field, checks `HasValue`, and only writes the ones the client actually mentioned. That's the entire mechanism.

In `Program.cs` you register the converter factory once so `System.Text.Json` knows how to read `PatchField<T>` from the wire:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));
```

Now the endpoint generated by `[CrudApi]` just calls `request.ApplyTo(player)` and persists — you don't touch `PatchField<T>` directly for the normal flow.

## Walkthrough — all three states in a real API

Continuing the schema from [Full CRUD with SQLite](/ZibStack.NET/guides/crud-sqlite/) where player 1 starts as:

```json
{
  "id": 1,
  "name": "Alice",
  "level": 55,
  "email": "alice@test.com"
}
```

### State 1 — not set: leave `email` alone

```bash
curl -X PATCH http://localhost:5000/api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"level":99}'

curl http://localhost:5000/api/players/1
```

```json
{
  "id": 1,
  "name": "Alice",
  "level": 99,
  "email": "alice@test.com"
}
```

The generated `UpdatePlayerRequest` has:
- `Level.HasValue = true, Level.Value = 99` → `target.Level = 99`
- `Email.HasValue = false` → skipped entirely

### State 2 — set to null: clear `email`

```bash
curl -X PATCH http://localhost:5000/api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"email":null}'

curl http://localhost:5000/api/players/1
```

```json
{
  "id": 1,
  "name": "Alice",
  "level": 99,
  "email": null
}
```

`Email.HasValue = true, Email.Value = null` → `target.Email = null`.

### State 3 — set to value: write a new email

```bash
curl -X PATCH http://localhost:5000/api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@newcompany.com"}'

curl http://localhost:5000/api/players/1
```

```json
{
  "id": 1,
  "name": "Alice",
  "level": 99,
  "email": "alice@newcompany.com"
}
```

`Email.HasValue = true, Email.Value = "alice@newcompany.com"` → `target.Email = "alice@newcompany.com"`.

**Three distinct outcomes from three JSON bodies that nullable-property DTOs could never have told apart.**

## Pattern matching on `PatchField<T>`

For non-trivial updates — audit logs, conditional business rules, validation that spans multiple fields — you often want to branch on the tri-state explicitly instead of just calling `ApplyTo`. C# pattern matching reads beautifully against `PatchField<T>`:

```csharp
public void ApplyEmailChange(UpdatePlayerRequest request, Player player, AuditLog audit)
{
    switch (request.Email)
    {
        case { HasValue: false }:
            // Client didn't touch the email field — no audit entry, no write
            break;

        case { HasValue: true, Value: null }:
            audit.Log($"Player {player.Id}: email cleared (was {player.Email})");
            player.Email = null;
            break;

        case { HasValue: true, Value: var v } when string.IsNullOrWhiteSpace(v):
            // Treat whitespace-only strings as "clear" — biz rule, not framework rule
            audit.Log($"Player {player.Id}: email cleared via whitespace input");
            player.Email = null;
            break;

        case { HasValue: true, Value: var v }:
            audit.Log($"Player {player.Id}: email {player.Email} → {v}");
            player.Email = v;
            break;
    }
}
```

The switch covers all three PatchField states **plus** a business rule (whitespace = clear), and each branch is isolated enough to audit separately. If a new state or rule appears later, you add a pattern without restructuring the method.

**Shorter variant** if all you need is "apply with audit" and you don't care about the whitespace edge case:

```csharp
if (request.Email is { HasValue: true, Value: var newEmail })
{
    audit.Log($"Player {player.Id}: email {player.Email} → {newEmail ?? "(cleared)"}");
    player.Email = newEmail;
}
```

The property pattern `{ HasValue: true, Value: var newEmail }` binds `newEmail` as `string?` (the wrapped type), so you can use it directly in the log line and assignment.

See [Core → Pattern matching with `PickXxx()`](/ZibStack.NET/packages/core/#pattern-matching-with-pickxxx) for more C# pattern-matching idioms that work well alongside `PatchField<T>`.

## Validation

`[ZValidate]` generates a `Validate()` method that walks `PatchField<T>` properties and only validates the ones that were actually set:

```csharp
[CrudApi]
[ZValidate]
public partial class Player
{
    [DtoIgnore] public int Id { get; set; }
    [ZRequired] [ZMinLength(2)] public required string Name { get; set; }
    [ZRange(1, 100)] public int Level { get; set; }
    [ZEmail]        public string? Email { get; set; }
}
```

Generated validation for the update DTO effectively says:

> "If `Name` was set, it must be non-empty and at least 2 chars. If `Level` was set, it must be 1..100. If `Email` was set **to a non-null value**, it must be a valid email (nulls bypass the `ZEmail` rule since 'clearing the email' is a legitimate operation)."

This matches what most REST clients expect: `[ZEmail]` on a patch DTO validates **format**, not presence. If you want "email must always be present", that rule belongs on the `[CreateDto]`, not on the update.

## How `PatchField<T>` shows up in OpenAPI

`ZibStack.NET.Dto` ships a schema filter for **Swashbuckle**. If you reference `Swashbuckle.AspNetCore`, `PatchField<string>` renders in Swagger UI as `string | null | omitted` with a clear description — clients see exactly the tri-state.

If you use the built-in `Microsoft.AspNetCore.OpenApi` (as in the CRUD guide) without Swashbuckle, `PatchField<T>` falls back to an opaque `{ hasValue, value }` object in the schema. Everything still works at runtime, but the schema is less pretty. Install `Swashbuckle.AspNetCore` if you publish a public API and care about schema clarity:

```bash
dotnet add package Swashbuckle.AspNetCore
```

The Dto generator auto-detects it and emits the schema filter automatically — no additional config needed.

## What `PatchField<T>` is *not*

A few things to keep in mind so you don't reach for `PatchField<T>` in the wrong places:

- **Not for create requests.** `[CreateDto]` intentionally uses required non-nullable properties — a POST should send the full object, tri-state makes no sense there.
- **Not for query parameters.** `[QueryDto]` uses plain nullable properties for filters because query strings have their own "not provided" semantics (absent key = no filter).
- **Not a replacement for RFC 6902 JSON Patch.** If you need explicit `add` / `remove` / `move` / `copy` / `test` operations (e.g. for array path manipulation), use a real JSON Patch library. `PatchField<T>` implements JSON Merge Patch (RFC 7396) done correctly, which is what most real REST APIs actually want.
- **Not reflected at runtime.** The generator emits `ApplyTo` as regular property access + conditional assignments. There's no reflection, no expression trees, no runtime type lookups. AOT-safe.

## Related reference

- [Dto — CRUD API & DTOs](/ZibStack.NET/packages/dto/) — `[CreateDto]` / `[UpdateDto]` / `[CrudApi]` full reference
- [Full CRUD with SQLite](/ZibStack.NET/guides/crud-sqlite/) — end-to-end project where `PatchField<T>` appears in the `PATCH` demo
- [Core → Pattern matching with `PickXxx()`](/ZibStack.NET/packages/core/#pattern-matching-with-pickxxx) — more C# pattern idioms
