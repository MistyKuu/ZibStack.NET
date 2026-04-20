---
title: Conditional & RuleSet
description: When/Unless conditional blocks and named RuleSet groups for selective validation.
---

## When — Conditional Validation

Rules inside `b.When()` only fire when the condition is true:

```csharp
public void Configure(IValidationBuilder<ShippingRequest> b)
{
    b.When(x => x.RequiresShipping, then =>
    {
        then.Property(x => x.ShippingAddress).Required();
        then.Property(x => x.PostalCode).Required().Match(@"^\d{5}$");
        then.Rule(x => !string.IsNullOrEmpty(x.City), "City is required for shipping");
    });
}
```

When `RequiresShipping == false` → all inner rules skipped.

## Unless — Inverse Conditional

Rules inside `b.Unless()` fire when the condition is **false**:

```csharp
public void Configure(IValidationBuilder<PaymentRequest> b)
{
    b.Unless(x => x.IsFreeOrder, then =>
    {
        then.Property(x => x.CardNumber).Required().CreditCard();
        then.Property(x => x.ExpiryDate).Required();
    });
}
```

`IsFreeOrder == true` → card rules skipped. `IsFreeOrder == false` → card required.

## RuleSet — Named Groups

Group rules and validate selectively:

```csharp
[ZValidate]
public partial class UserDto : IValidationConfigurator<UserDto>
{
    [ZRequired] public string Name { get; set; } = "";
    [ZRequired] [ZEmail] public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public int Id { get; set; }

    public void Configure(IValidationBuilder<UserDto> b)
    {
        b.RuleSet("Create", set =>
        {
            set.Rule(x => !string.IsNullOrEmpty(x.Password), "Password required for creation");
            set.Rule(x => x.Password.Length >= 8, "Password must be at least 8 characters");
        });

        b.RuleSet("Update", set =>
        {
            set.Rule(x => x.Id > 0, "Id required for update");
        });
    }
}
```

### Usage:

```csharp
// Validate ALL rules (attributes + base rules + all rulesets):
var result = dto.Validate();

// Validate only "Create" ruleset (+ base attribute/expression rules):
var result = dto.Validate(null, "Create");

// Validate only "Update" ruleset:
var result = dto.Validate(null, "Update");

// Multiple sets:
var result = dto.Validate(null, "Create", "Update");
```

Base rules (attributes + `b.Rule()` outside any set) **always** run. RuleSets are additive.

### When to use RuleSet

| Scenario | Approach |
|----------|----------|
| Same DTO for create + update | `b.RuleSet("Create", ...)` for password, `b.RuleSet("Update", ...)` for Id |
| Multi-step wizard | One set per step: "Step1", "Step2", "Step3" |
| Admin vs User validation | `b.RuleSet("Admin", ...)` with stricter rules |

## Combining When + RuleSet

```csharp
public void Configure(IValidationBuilder<OrderDto> b)
{
    b.RuleSet("Shipping", set =>
    {
        set.When(x => x.DeliveryMethod == "express", then =>
        {
            then.Rule(x => x.ExpressAddress != null, "Express address required");
        });
    });
}
```

Validates only when: you request "Shipping" set AND `DeliveryMethod == "express"`.
