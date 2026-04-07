# ZibStack.NET.Utils

A C# source generator that provides generic utility types for .NET projects. No reflection, no runtime overhead.

## Install

```
dotnet add package ZibStack.NET.Utils
```

## What's included

### `PatchField<T>`

A readonly struct that wraps a value with a `HasValue`/`Value` distinction — distinguishes between "not sent", "sent with value", and "sent as null":

```csharp
using ZibStack.NET.Utils;

var field = new PatchField<string>("hello");
field.HasValue // true
field.Value    // "hello"

// Implicit operators
PatchField<int> f = 42;
int value = f;
```

### JSON converters

The generator detects which serializers are available and produces the corresponding converters:

| Serializer | Generated converter | Registration |
|---|---|---|
| System.Text.Json | `PatchFieldJsonConverterFactory` | `options.Converters.Add(new PatchFieldJsonConverterFactory())` |
| Newtonsoft.Json | `PatchFieldNewtonsoftConverter` | `settings.Converters.Add(new PatchFieldNewtonsoftConverter())` |

### Swagger / OpenAPI

When Swashbuckle is detected, the generator emits a `PatchFieldSchemaFilter` that unwraps `PatchField<T>` to its inner type:

```csharp
builder.Services.AddSwaggerGen(c => c.SchemaFilter<PatchFieldSchemaFilter>());
```

### `PaginatedResponse<T>`

Generic paginated response wrapper:

```csharp
var page = PaginatedResponse<Product>.Create(items, totalCount: 100, page: 2, pageSize: 10);
var page = await PaginatedResponse<Product>.CreateAsync(query, page: 1, pageSize: 20);
var mapped = page.Map(p => new ProductDto(p));
```

### `SortDirection`

```csharp
public enum SortDirection { Asc, Desc }
```

### `[PartialFrom(typeof(T))]`

Like TypeScript's `Partial<T>` — generates a class where every property is a `PatchField<T>` with an `ApplyTo()` method:

```csharp
[PartialFrom(typeof(Player))]
public partial record PartialPlayer;

// Generated: PatchField properties for all public properties of Player + ApplyTo(Player)
```

### `[IntersectFrom(typeof(T))]`

Like TypeScript's `&` operator — combine properties from multiple types:

```csharp
[IntersectFrom(typeof(Player))]
[IntersectFrom(typeof(Address))]
public partial record PlayerWithAddress;

// Generated: all properties from both types + ApplyTo(Player) + ApplyTo(Address)
```

### `[PickFrom(typeof(T), ...)]`

Like TypeScript's `Pick<T, K>` — whitelist of properties:

```csharp
[PickFrom(typeof(Player), nameof(Player.Name), nameof(Player.Level))]
public partial record PlayerSummary;
```

### `[OmitFrom(typeof(T), ...)]`

Like TypeScript's `Omit<T, K>` — exclude listed properties:

```csharp
[OmitFrom(typeof(Player), nameof(Player.Id), nameof(Player.CreatedAt))]
public partial record PlayerWithoutMeta;
```

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects)

## License

MIT
