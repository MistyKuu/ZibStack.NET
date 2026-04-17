---
title: Dto — Generated integration tests
description: "[assembly: GenerateCrudTests] auto-generates xUnit integration tests for every [CrudApi] entity — CRUD, bulk, query DSL, nested relations, collection any/all, complex AND/OR filters. Bogus/Faker for validation-aware test data."
---

Add the `[assembly: GenerateCrudTests]` attribute in your test project to auto-generate xUnit integration tests for every `[CrudApi]` entity:

```csharp
// In your test project (e.g., AssemblyInfo.cs or any file)
[assembly: GenerateCrudTests]
```

The generator scans referenced assemblies for `[CrudApi]` entities and emits a comprehensive test class per entity. For the sample project (2 entities: `Player` + `Team`) this produces 25 tests total — the exact count per entity depends on which features are enabled (bulk operations, Query DSL, navigation properties).

Tests auto-regenerate on every build. **Do not edit the generated files** — write custom tests in separate files instead. See the sample project for a working example: `packages/ZibStack.NET.Dto/sample/SampleApi.Tests/`.

## Test data: Bogus (Faker)

Request bodies are built with [Bogus](https://github.com/bmarber/Bogus). The generator reads validation attributes on entity properties and maps them to the correct Faker method:

| Validation attribute(s) | Generated Faker call |
|---|---|
| `[ZEmail]` | `_faker.Internet.Email()` |
| `[ZRange(1, 100)]` | `_faker.Random.Int(1, 100)` |
| `[ZMinLength(2)]` + `[ZMaxLength(50)]` | `_faker.Random.String2(2, 50)` |
| `[ZMaxLength(N)]` only | `_faker.Random.String2(1, N)` |
| `[ZMinLength(N)]` only | `_faker.Random.String2(N, 50)` |
| `decimal` property | `_faker.Finance.Amount(1, 1000)` |
| No constraints (string) | `_faker.Random.String2(1, 100)` |

This means every generated test payload is syntactically valid and satisfies your validation rules out of the box.

## Generated test categories

### 1. CRUD tests (4 per entity, always generated)

Every `[CrudApi]` entity gets these core tests:

| Test | What it does |
|---|---|
| `GetList_ReturnsItemsArray` | `GET /api/{entities}` returns 200 with `items` array in the response body |
| `GetById_NonExistent_ReturnsNotFound` | `GET /api/{entities}/999999` returns 404 |
| `Create_PersistsAndReturnsLocation` | `POST` with Faker payload, asserts 201, then `GET` on the `Location` header verifies the entity actually exists |
| `FullCrudCycle_CreateReadUpdateDelete` | Creates, reads back, updates with new Faker values, reads back again and asserts `Assert.NotEqual(before, after)` on the first string field, deletes, then verifies `GET` returns 404 |

The full CRUD cycle test covers the entire lifecycle in one test — create, read, update, read-back-and-verify-change, delete, verify-gone:

```csharp
// Excerpt from generated PlayerCrudTests.FullCrudCycle_CreateReadUpdateDelete
// 1. Create
var createResponse = await _client.PostAsJsonAsync("/api/players",
    new { Name = _faker.Random.String2(2, 50), Level = _faker.Random.Int(1, 100),
          Salary = _faker.Finance.Amount(1, 1000), Password = _faker.Random.String2(8, 50) });
Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
var location = createResponse.Headers.Location!.ToString();

// 2. Read back
var created = await (await _client.GetAsync(location))
    .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

// 3. Update with new values, verify change stuck
var patchContent = JsonContent.Create(new { Name = _faker.Random.String2(2, 50),
    Level = _faker.Random.Int(1, 100), Email = _faker.Internet.Email() });
await _client.PatchAsync(location, patchContent);
var updated = await (await _client.GetAsync(location))
    .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
Assert.NotEqual(created.GetProperty("name").GetString(),
                updated.GetProperty("name").GetString());

// 4. Delete + verify 404
await _client.DeleteAsync(location);
Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(location)).StatusCode);
```

### 2. Bulk tests (2, when `BulkCreate` / `BulkDelete` flags are set)

Generated when `CrudOperations.BulkCreate` or `CrudOperations.BulkDelete` is enabled:

| Test | What it does |
|---|---|
| `BulkCreate_ActuallyCreatesItems` | Gets count before, bulk-creates 3 items, verifies count increased by exactly 3 |
| `BulkDelete_ActuallyDeletesItems` | Creates 3 items, collects their IDs, bulk-deletes, then verifies each deleted ID returns 404 |

### 3. Query DSL tests (2, when `ZibStack.NET.Query` is referenced)

Generated when the project references `ZibStack.NET.Query` (the Query DSL package):

| Test | What it does |
|---|---|
| `QueryCount_ReturnsCorrectCount` | Gets `?count=true` before and after creating 2 items, asserts the count increased by at least 2 |
| `QuerySelect_ReturnsOnlySelectedFields` | Requests `?select=Name`, asserts the selected field (`name`) is present and a non-selected field (`level` / `description`) is absent |

### 4. Nested relation tests (2, when `[OneToOne]` navigation detected)

Generated on the entity that has a `[OneToOne]` navigation property (e.g., `Player.Team`):

| Test | What it does |
|---|---|
| `NestedFilter_Team_Name_ContainsFilter` | Filters with `?filter=Team.Name=*ZZZZNONEXISTENT`, verifies filtered count is <= unfiltered count |
| `NestedSort_Team_Name` | Sorts with `?sort=Team.Name`, verifies the response contains `totalCount` |

### 5. Collection any/all tests (3, when `[OneToMany]` navigation detected)

Generated on the **parent** entity that has a `[OneToMany]` navigation (e.g., `Team.Players`):

| Test | What it does |
|---|---|
| `CollectionAny_{Nav}_{IntProp}_VerifiesRelation` | Creates parent, creates child with FK + `Level = 99`, queries `?filter=Players.any.Level>=99`, asserts `Assert.Contains(parentId)` in results |
| `CollectionAll_{Nav}_{IntProp}_VerifiesRelation` | Same setup, queries `?filter=Players.all.Level>=99`, asserts parent is in results |
| `CollectionAny_{Nav}_{StringProp}_Contains` | Creates parent, creates child with `Name = "UNIQUE_SEARCH_TERM"`, queries `?filter=Players.any.Name=*UNIQUE_SEARCH`, asserts parent is in results |

### 6. Complex filter tests (3, when Query DSL is available + entity has a numeric field)

Generated when `ZibStack.NET.Query` is referenced and the entity has at least one filterable `int` property:

| Test | What it does |
|---|---|
| `ComplexFilter_AND` | Creates item with known values (`Level=99`, `Name=ANDTEST_...`), filters with `Level>=99,Name=*ANDTEST`, verifies all returned items have `Level >= 99` |
| `ComplexFilter_OR` | Filters with `Level>=99\|Level<=1`, verifies each returned item matches either condition |
| `ComplexFilter_GroupedAND_OR` | Creates item, filters with `(Level>=99\|Level<=1),Name=*GROUPTEST`, verifies all returned items satisfy the grouped predicate |

### 7. Soft-delete tests (when `SoftDelete = true`)

Additional tests verify that `DELETE` sets flags instead of removing the row, and that `?includeDeleted=true` returns deleted entities.

## Test count breakdown (sample project)

| Entity | CRUD | Bulk | Query DSL | Nested nav | Collection any/all | Complex filter | Total |
|---|---|---|---|---|---|---|---|
| `Player` | 4 | 2 | 2 | 2 | -- | 3 | **13** |
| `Team` | 4 | -- | 2 | -- | 3 | 3 | **12** |
| | | | | | | **Total** | **25** |

`Player` gets bulk tests (entity has `CrudOperations.AllWithBulk`) and nested-nav tests (has `[OneToOne] Team`). `Team` gets collection any/all tests (has `[OneToMany] Players`). Both get Query DSL and complex filter tests because the project references `ZibStack.NET.Query`.

## Extending generated tests

Generated test classes are `partial`, so you can add custom test methods alongside the generated ones in a separate file:

```csharp
public partial class PlayerCrudTests
{
    [Fact]
    public async Task GetList_FiltersByLevel()
    {
        // custom test using the same WebApplicationFactory
        var response = await _client.GetAsync("/api/players?filter=Level>=5");
        response.EnsureSuccessStatusCode();
    }
}
```

## Setup

The test project needs references to `Microsoft.AspNetCore.Mvc.Testing`, `xunit`, and `Bogus`:

```
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package xunit
dotnet add package Bogus
```
