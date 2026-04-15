---
title: Database integration
description: EF Core entity configuration, [Entity] mapping, and cross-cutting database concerns wired from the same UI metadata.
---

## EF Core Integration (`[Entity]`)

Add `[Entity]` to any model class to generate `IEntityTypeConfiguration<T>` at compile time. The same class serves as both UI model and EF Core entity — no separate entity class needed.

Requires `Microsoft.EntityFrameworkCore` and a relational provider (e.g. `Microsoft.EntityFrameworkCore.SqlServer`) in your project. Generation is skipped automatically if EF Core is not referenced.

```csharp
[UiForm]
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/project")]
[Entity(TableName = "Projects")]
public partial class ProjectView
{
    public int Id { get; set; }

    [UiFormField(Label = "Project Name")]
    [UiTableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    public int SettingsId { get; set; }

    [Computed]
    public int TaskCount { get; set; }

    [OneToMany(Label = "Tasks")]
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    [OneToOne(Label = "Settings")]
    public ProjectSettings? Settings { get; set; }
}
```

Generated at compile time:

```csharp
partial class ProjectView : IEntityTypeConfiguration<ProjectView>
{
    void IEntityTypeConfiguration<ProjectView>.Configure(EntityTypeBuilder<ProjectView> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(e => e.Id);
        builder.Ignore(e => e.TaskCount);
        builder.HasMany(e => e.Tasks).WithOne().HasForeignKey("ProjectId");
        builder.HasOne(e => e.Settings).WithOne().HasForeignKey<ProjectView>(e => e.SettingsId);
    }
}
```

Register all generated configurations in your `DbContext`:

```csharp
public class AppDbContext : DbContext
{
    public DbSet<ProjectView> Projects => Set<ProjectView>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder builder)
        => builder.ApplyGeneratedConfigurations();
}
```

### What gets generated

| Source | EF Core output |
|--------|---------------|
| `[Entity(TableName = "X")]` | `builder.ToTable("X")` |
| `[Entity(Schema = "dbo")]` | `builder.ToTable("...", "dbo")` |
| Property named `Id` or `{Class}Id` | `builder.HasKey(e => e.Id)` |
| `[Computed]` | `builder.Ignore(e => e.Prop)` |
| `[OneToMany]` on `ICollection<T>` | `builder.HasMany(e => e.Nav).WithOne().HasForeignKey(...)` |
| `[OneToOne]` on navigation prop | `builder.HasOne(e => e.Nav).WithOne().HasForeignKey<T>(...)` |

## Database Integration

ZibStack.NET.UI is **DB-agnostic** — it works with any data source. The UI metadata lives on **DTOs/ViewModels**, not on entities, because forms and tables almost never map 1:1 to DB tables.

### Recommended pattern: Entity → DTO → UI metadata

```
EF Core Entity (DB)  →  ZibStack.NET.Dto (generates DTOs)  →  ZibStack.NET.UI (generates UI metadata)  →  JSON  →  Frontend
```

#### 1. Define your entity (database)

```csharp
// Entity — maps to DB table
public class Voivodeship
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public int Population { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }  // soft delete — not on any form

    // Navigation properties
    public ICollection<County> Counties { get; set; } = new List<County>();
}

public class County
{
    public int Id { get; set; }
    public int VoivodeshipId { get; set; }
    public string Name { get; set; } = "";
    public int Population { get; set; }
    public Voivodeship Voivodeship { get; set; } = null!;
}
```

#### 2. Define view models with UI metadata

```csharp
// Create form — only fields the user should fill in, with validation
[UiForm]
[UiFormGroup("basic", Label = "Basic Info")]
public partial class CreateVoivodeshipRequest
{
    [ZRequired] [ZMinLength(2)] [ZMaxLength(100)]
    [UiFormField(Label = "Name", Placeholder = "e.g. California")]
    public required string Name { get; set; }

    [ZRequired] [ZMatch(@"^[A-Z]{2}$")]
    [UiFormField(Label = "Code", Placeholder = "e.g. CA", HelpText = "Two-letter code")]
    public required string Code { get; set; }

    [ZRange(0, 100_000_000)]
    [UiFormField(Label = "Population")]
    public int Population { get; set; }
}

// Child table — declares its own SchemaUrl
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/county")]
public partial class CountyTableView
{
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }
    public int VoivodeshipId { get; set; }
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";
    [UiTableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }
}

[UiTable(DefaultSort = "Name", DefaultPageSize = 50, SchemaUrl = "/api/tables/voivodeship")]
[RowAction("edit", Label = "Edit", Endpoint = "/api/voivodeships/{id}")]
[ToolbarAction("export", Label = "Export", Endpoint = "/api/voivodeships/export",
               SelectionMode = "multiple")]
public partial class VoivodeshipTableView
{
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [UiTableColumn(Sortable = true)]
    public string Code { get; set; } = "";

    [UiTableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }

    [UiTableColumn(Sortable = true)]
    [Computed]
    public int CountyCount { get; set; }

    [UiTableColumn(Sortable = true, Format = "yyyy-MM-dd")]
    public DateTime CreatedAt { get; set; }
}
```

#### 3. Wire up with EF Core in your API

```csharp
// Serve schemas
app.MapGet("/api/forms/voivodeship", () =>
    Results.Content(CreateVoivodeshipRequest.GetFormSchemaJson(), "application/json"));

app.MapGet("/api/tables/voivodeship", () =>
    Results.Content(VoivodeshipTableView.GetTableSchemaJson(), "application/json"));

// CRUD endpoints
app.MapGet("/api/voivodeships", async (AppDbContext db) =>
{
    var data = await db.Voivodeships
        .Where(v => !v.IsDeleted)
        .Select(v => new VoivodeshipTableView
        {
            Id = v.Id,
            Name = v.Name,
            Code = v.Code,
            Population = v.Population,
            CountyCount = v.Counties.Count,  // computed in SQL
            CreatedAt = v.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(data);
});

app.MapPost("/api/voivodeships", async (CreateVoivodeshipRequest request, AppDbContext db) =>
{
    var entity = new Voivodeship
    {
        Name = request.Name,
        Code = request.Code,
        Population = request.Population,
        CreatedAt = DateTime.UtcNow
    };
    db.Voivodeships.Add(entity);
    await db.SaveChangesAsync();
    return Results.Created($"/api/voivodeships/{entity.Id}", entity.Id);
});

// Child table — filtered by parent ID
app.MapGet("/api/voivodeships/{voivodeshipId}/counties", async (int voivodeshipId, AppDbContext db) =>
{
    var data = await db.Counties
        .Where(c => c.VoivodeshipId == voivodeshipId)
        .Select(c => new CountyTableView
        {
            Id = c.Id,
            VoivodeshipId = c.VoivodeshipId,
            Name = c.Name,
            Population = c.Population
        })
        .ToListAsync();
    return Results.Ok(data);
});
```

The key insight: **entity has internal fields** (IsDeleted, UpdatedAt, navigation properties), **create form has user-facing fields** (Name, Code, Population), **table view has computed columns** (CountyCount). Each serves a different purpose — ZibStack.NET.UI annotates the view models, not the entities.

### Without EF Core

Works the same with Dapper, ADO.NET, REST APIs, or any data source — just annotate your DTOs/ViewModels:

```csharp
[UiForm]
[UiTable(DefaultSort = "Name")]
public partial class ProductView
{
    [UiFormField(Label = "Name")]
    [UiTableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    [Slider(Min = 0, Max = 10000)]
    [UiTableColumn(Sortable = true)]
    [ColumnStyle(When = "value < 10", Severity = "warning")]
    public int Stock { get; set; }
}

// With Dapper:
app.MapGet("/api/products", async (IDbConnection db) =>
{
    var data = await db.QueryAsync<ProductView>("SELECT Name, Stock FROM Products");
    return Results.Ok(data);
});
```

