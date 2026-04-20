---
title: Fluent Rules
description: Cross-field validation, custom expressions, and per-property rule chains via IValidationConfigurator<T>.
---

## Overview

Implement `IValidationConfigurator<T>` for rules that go beyond single-property attributes. The generator reads your `Configure()` body at compile time — it is **never invoked at runtime**.

```csharp
[ZValidate]
public partial class OrderRequest : IValidationConfigurator<OrderRequest>
{
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public List<string> Items { get; set; } = new();

    public void Configure(IValidationBuilder<OrderRequest> b)
    {
        b.Rule(x => x.Items.Count > 0, "Order must have at least one item");
        b.Rule(x => x.Discount >= 0 && x.Discount <= x.Subtotal,
            "Discount cannot exceed subtotal");
        b.Rule(x => x.Total == x.Subtotal - x.Discount,
            "Total must equal Subtotal minus Discount");
    }
}
```

## Custom Expressions — `b.Rule()`

Any C# expression that compiles in the class context:

```csharp
b.Rule(x => x.EndDate > x.StartDate, "End must be after start");
b.Rule(x => x.ShipBy == null || x.ShipBy > DateTime.UtcNow, "Ship date must be in the future");
b.Rule(x => x.Tags.All(t => t.Length <= 20), "Each tag must be at most 20 chars");
```

## Per-Property Chains — `b.Property()`

Fluent equivalent of stacking attributes:

```csharp
b.Property(x => x.Username)
    .Required()
    .MinLength(3)
    .MaxLength(20)
    .Match(@"^[a-zA-Z0-9_]+$", "Only letters, numbers, underscores");

b.Property(x => x.Email)
    .Required()
    .Email();

b.Property(x => x.Role)
    .In("admin", "user", "viewer");

b.Property(x => x.CardNumber)
    .CreditCard();
```

Available fluent methods: `.Required()`, `.Email()`, `.Url()`, `.NotEmpty()`, `.MinLength(n)`, `.MaxLength(n)`, `.Range(min, max)`, `.Match(pattern)`, `.In(values)`, `.NotIn(values)`, `.CreditCard()`, `.Phone()`.

## Cross-Field Comparisons

```csharp
b.Property(x => x.ConfirmPassword).EqualTo(x => x.Password, "Passwords must match");
b.Property(x => x.EndDate).GreaterThan(x => x.StartDate);
b.Property(x => x.Max).GreaterThanOrEqual(x => x.Min);
```

| Method | Description |
|--------|-------------|
| `.GreaterThan(x => x.Other)` | `>` |
| `.GreaterThanOrEqual(x => x.Other)` | `>=` |
| `.LessThan(x => x.Other)` | `<` |
| `.LessThanOrEqual(x => x.Other)` | `<=` |
| `.EqualTo(x => x.Other)` | `==` |
| `.NotEqualTo(x => x.Other)` | `!=` |

All accept an optional message as second argument.
