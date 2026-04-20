---
title: Nested & Collections
description: Automatic recursive validation for nested objects and collections with property path tracking.
---

## Nested Object Validation

Properties whose type has `[ZValidate]` are automatically validated recursively:

```csharp
[ZValidate]
public partial class Address
{
    [ZRequired] public string Street { get; set; } = "";
    [ZRequired] public string City { get; set; } = "";
    [ZMatch(@"^\d{5}$")] public string? Zip { get; set; }
}

[ZValidate]
public partial class CustomerForm
{
    [ZRequired] public string Name { get; set; } = "";
    public Address BillingAddress { get; set; } = new();
    public Address? ShippingAddress { get; set; }  // null → skipped
}
```

```csharp
var form = new CustomerForm
{
    Name = "",
    BillingAddress = new Address { Street = "", City = "" }
};
var result = form.Validate();

// Errors with dot-separated paths:
// "Name: Name is required."
// "BillingAddress.Street: Street is required."
// "BillingAddress.City: City is required."
```

**Nullable properties** (`Address?`) are skipped when `null`. Non-nullable properties are always validated.

## Collection Validation

Collections of `[ZValidate]` types are validated element-by-element with index:

```csharp
[ZValidate]
public partial class LineItem
{
    [ZRequired] public string Sku { get; set; } = "";
    [ZRange(1, 9999)] public int Quantity { get; set; }
}

[ZValidate]
public partial class Invoice
{
    [ZRequired] public string InvoiceNumber { get; set; } = "";
    public List<LineItem> Lines { get; set; } = new();
}
```

```csharp
var invoice = new Invoice
{
    InvoiceNumber = "INV-001",
    Lines = new()
    {
        new LineItem { Sku = "", Quantity = 0 },
        new LineItem { Sku = "ABC", Quantity = 5 },
        new LineItem { Sku = "", Quantity = -1 },
    }
};
var result = invoice.Validate();

// Errors:
// "Lines[0].Sku: Sku is required."
// "Lines[0].Quantity: Quantity must be between 1 and 9999."
// "Lines[2].Sku: Sku is required."
// "Lines[2].Quantity: Quantity must be between 1 and 9999."
```

## ValidationContext

Context is auto-created at the root and flows through nested validators:

```csharp
// Simple — context created internally:
var result = form.Validate();

// With custom data:
var result = form.Validate(new ValidationContext
{
    Items = { ["tenant"] = "acme", ["userId"] = 42 }
});
```

| Property | Type | Description |
|----------|------|-------------|
| `Parent` | `object?` | The object that triggered this nested validation |
| `Path` | `string` | Dot-separated path from root (e.g. `"BillingAddress.Street"`) |
| `RootObject` | `object?` | The top-level object that started the chain |
| `Items` | `Dictionary<string, object?>` | User-defined data bag |

## ValidationResult

```csharp
public sealed class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationError> ValidationErrors { get; }
    public IReadOnlyList<string> Errors { get; }  // flat strings

    // ASP.NET ModelState shape:
    public Dictionary<string, List<string>> ToDictionary();
}

public sealed class ValidationError
{
    public string Property { get; }   // "BillingAddress.Street"
    public string Message { get; }    // "Street is required."
    public string FullMessage { get; } // "BillingAddress.Street: Street is required."
}
```

### ToDictionary()

```csharp
var dict = result.ToDictionary();
// {
//   "Name": ["Name is required."],
//   "BillingAddress.Street": ["Street is required."],
//   "BillingAddress.City": ["City is required."]
// }
```

Matches ASP.NET `ModelStateDictionary` shape — drop directly into `ValidationProblem()`.
