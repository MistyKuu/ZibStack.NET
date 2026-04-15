---
title: "External types ([CreateDtoFor] / [UpdateDtoFor])"
description: Generate request DTOs for types you don't own — partial records bound to an external entity, with ToEntity / ApplyTo wiring.
---

## External types (`[CreateDtoFor]` / `[UpdateDtoFor]`)

For classes you don't control (e.g. from a NuGet package), use separate attributes for create and update:

```csharp
// External class you can't modify
public class ExternalOrder
{
    public int Id { get; set; }
    public string ProductName { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}

// Create DTO — ignore Id, require ProductName
[CreateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" }, Required = new[] { "ProductName" })]
public partial record CreateOrderRequest;

// Update DTO — ignore Id
[UpdateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" })]
public partial record UpdateOrderRequest;
```

The generator produces a `partial record` with `PatchField` properties for all non-ignored properties from the target type.

- `[CreateDtoFor]` generates: `Validate()` (checks Required fields) + `ToEntity()`
- `[UpdateDtoFor]` generates: `Validate()` (null checks only) + `ApplyTo(target)`

You control the class name, which properties to ignore, and which to make required.

### Renaming properties

Use `[DtoName("newName")]` on the property, or fluent
`b.ForType<T>().Property(p => p.X).RenameTo("newName")` — see
[Fluent configuration](#fluent-configuration-idtoconfigurator).

