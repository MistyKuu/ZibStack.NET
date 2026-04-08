# ZibStack.NET.Dapper

Dapper integration for ZibStack.NET.Dto CRUD API — provides a `DapperCrudStore<TEntity, TKey>` base class via source generation.

## Install

```
dotnet add package ZibStack.NET.Dapper
```

## Quick Start

```csharp
public class PlayerStore : DapperCrudStore<Player, int>
{
    public PlayerStore(IDbConnection db) : base(db) { }
    protected override string TableName => "Players";
}

// Program.cs
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqliteConnection("Data Source=app.db"));
builder.Services.AddScoped<ICrudStore<Player, int>, PlayerStore>();
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/dapper/](https://mistykuu.github.io/ZibStack.NET/packages/dapper/)
