---
title: ZibStack.NET.Dapper
description: Dapper integration for ZibStack.NET.Dto CRUD API with a source-generated DapperCrudStore base class.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Dapper.svg)](https://www.nuget.org/packages/ZibStack.NET.Dapper) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Dapper)

Dapper integration for [ZibStack.NET.Dto](https://www.nuget.org/packages/ZibStack.NET.Dto) CRUD API. Provides a `DapperCrudStore<TEntity, TKey>` base class via source generation.

## Install

```
dotnet add package ZibStack.NET.Dto
dotnet add package ZibStack.NET.Dapper
dotnet add package Dapper
```

## Quick start

1. Define your entity with `[CrudApi]`:

```csharp
[CrudApi]
[CreateDto]
[UpdateDto]
[ResponseDto]
public class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
}
```

2. Implement the store:

```csharp
using ZibStack.NET.Dapper;

public class PlayerStore : DapperCrudStore<Player, int>
{
    public PlayerStore(IDbConnection db) : base(db) { }
    protected override string TableName => "Players";
}
```

3. Register in DI:

```csharp
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqliteConnection("Data Source=app.db"));
builder.Services.AddScoped<ICrudStore<Player, int>, PlayerStore>();
```

## `DapperCrudStore<TEntity, TKey>`

Base class implementing `ICrudStore<TEntity, TKey>` using Dapper:

| Method | SQL |
|--------|-----|
| `GetByIdAsync` | `SELECT * FROM {Table} WHERE {Key} = @Id` |
| `Query` | `SELECT * FROM {Table}` (returns in-memory `IQueryable`) |
| `CreateAsync` | `INSERT INTO {Table} (columns...) VALUES (@params...)` |
| `UpdateAsync` | `UPDATE {Table} SET col = @col, ... WHERE {Key} = @Key` |
| `DeleteAsync` | `DELETE FROM {Table} WHERE {Key} = @Key` |

### Configuration

Override virtual properties to customize mapping:

| Property | Default | Description |
|----------|---------|-------------|
| `TableName` | entity type name + "s" | Table name used in SQL |
| `KeyColumn` | `"Id"` | Primary key column name |

### Custom queries

All methods are `virtual` — override any operation for custom SQL:

```csharp
public class PlayerStore : DapperCrudStore<Player, int>
{
    public PlayerStore(IDbConnection db) : base(db) { }
    protected override string TableName => "Players";

    public override async ValueTask<Player?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM Players WHERE Id = @Id AND IsDeleted = 0";
        return await SqlMapper.QueryFirstOrDefaultAsync<Player>(Db,
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }
}
```

## How it works

This package is a **source generator**. When your project references both `ZibStack.NET.Dto` (which provides `ICrudStore`) and `Dapper`, the generator emits the `DapperCrudStore` base class into your compilation. No runtime dependency on this package.

## Limitations

- `Query()` loads all rows into memory and returns `IQueryable` over the in-memory collection. For large tables, override `Query()` with a custom implementation or use filtering at the SQL level.
- `CreateAsync` skips the key column (assumes auto-increment). Override for composite keys or non-auto-increment scenarios.
- Column names are derived from property names via reflection. Override individual methods if your column names differ from property names.
