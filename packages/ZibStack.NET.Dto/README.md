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

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/dto/](https://mistykuu.github.io/ZibStack.NET/packages/dto/)
