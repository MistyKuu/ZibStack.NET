# ZibStack.NET.Dto

Source generator that produces strongly-typed CRUD DTOs and full API endpoints from your domain models — no reflection, no runtime overhead.

## Install

```
dotnet add package ZibStack.NET.Dto
```

## Quick Start

```csharp
[CrudApi]
public class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
}

// Program.cs
app.MapPlayerEndpoints(); // full CRUD API — auto-generates DTOs + endpoints
```

## Generated Integration Tests

Add `[assembly: GenerateCrudTests]` in your test project — auto-generates xUnit tests for every `[CrudApi]` entity (CRUD, bulk, query DSL, nested relations).

Generated test classes are `partial` with hooks for customization:

```csharp
public partial class PlayerCrudTests
{
    static partial void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(s => s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("test")));

    static partial void ConfigureClient(HttpClient client)
        => client.DefaultRequestHeaders.Authorization = new("Bearer", "test-token");
}
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/dto/](https://mistykuu.github.io/ZibStack.NET/packages/dto/)
