---
title: ZibStack.NET.Validation
description: Source-generated validation for .NET — add validation attributes, get a compile-time Validate() method with no reflection or runtime overhead.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Validation.svg)](https://www.nuget.org/packages/ZibStack.NET.Validation) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Validation)

Source-generated validation for .NET. Decorate your models with attributes, optionally add fluent rules, and the generator emits a compile-time `Validate()` method — **zero reflection, zero runtime overhead**.

## Install

```bash
dotnet add package ZibStack.NET.Validation
```

## Quick Start

```csharp
using ZibStack.NET.Validation;

[ZValidate]
public partial class CreateUserRequest
{
    [ZRequired]
    [ZMinLength(2)]
    [ZMaxLength(50)]
    public string Name { get; set; } = "";

    [ZRequired]
    [ZEmail]
    public string Email { get; set; } = "";

    [ZRange(18, 120)]
    public int Age { get; set; }

    [ZUrl]
    public string? Website { get; set; }
}
```

Usage:

```csharp
var request = new CreateUserRequest { Name = "", Email = "bad", Age = 10 };
var result = request.Validate();

if (!result.IsValid)
{
    foreach (var error in result.ValidationErrors)
        Console.WriteLine($"{error.Property}: {error.Message}");
}
// Output:
// Name: Name is required.
// Name: Name must be at least 2 characters.
// Email: Email must be a valid email address.
// Age: Age must be between 18 and 120.
```

## Validation Attributes

All attributes live in the `ZibStack.NET.Validation` namespace and are processed at compile time.

| Attribute | Applies to | Description |
|-----------|-----------|-------------|
| `[ZValidate]` | class / record / struct | Marks the type for code generation (must be `partial`) |
| `[ZRequired]` | property | Not null; for strings also not empty/whitespace |
| `[ZMinLength(n)]` | property (string / collection) | Minimum length or item count |
| `[ZMaxLength(n)]` | property (string / collection) | Maximum length or item count |
| `[ZRange(min, max)]` | property (numeric) | Inclusive numeric range |
| `[ZEmail]` | property (string) | Must match email regex |
| `[ZUrl]` | property (string) | Must be a valid absolute URI (`Uri.TryCreate`) |
| `[ZMatch(pattern)]` | property (string) | Must match the given regex pattern |
| `[ZNotEmpty]` | property (collection / string) | Collection must have items; string must not be whitespace |
| `[ZIn("a","b","c")]` | property | Value must be one of the allowed values |
| `[ZNotIn("x","y")]` | property | Value must NOT be any of the specified values |
| `[ZCreditCard]` | property (string) | Must pass the Luhn algorithm check |
| `[ZPhone]` | property (string) | Must match phone number format regex |
| `[ZCascade]` | property | Stop after first rule failure for this property |

### Custom error messages

All validation attributes accept a `Message` parameter:

```csharp
[ZRequired(Message = "Please provide your name")]
[ZMatch(@"^\d{3}-\d{4}$", Message = "Phone must be in format XXX-XXXX")]
[ZRange(1, 100, Message = "{PropertyName} must be between 1 and 100, got {PropertyValue}")]
```

## Fluent Rules (IValidationConfigurator&lt;T&gt;)

For rules that go beyond single-property attributes, implement `IValidationConfigurator<T>`. The generator reads your `Configure()` method at compile time and inlines the logic into the generated `Validate()` — it is **never invoked at runtime**.

```csharp
[ZValidate]
public partial class OrderRequest : IValidationConfigurator<OrderRequest>
{
    // ... properties ...

    public void Configure(IValidationBuilder<OrderRequest> b)
    {
        // rules go here
    }
}
```

### Custom expressions

Use `b.Rule()` for free-form boolean expressions:

```csharp
public void Configure(IValidationBuilder<OrderRequest> b)
{
    b.Rule(x => x.Items.Count > 0, "Order must have at least one item");
    b.Rule(x => x.Discount >= 0 && x.Discount <= x.Subtotal,
        "Discount cannot exceed subtotal");
    b.Rule(x => x.Total == x.Subtotal - x.Discount,
        "Total must equal Subtotal minus Discount");
    b.Rule(x => x.ShipBy == null || x.ShipBy > x.CreatedAt,
        "ShipBy must be after CreatedAt");
}
```

Any expression that compiles as C# in the class context works — property access, arithmetic, null checks, `&&`/`||`, method calls.

### Per-property chains

Use `b.Property()` to attach fluent rules to a specific property:

```csharp
public void Configure(IValidationBuilder<RegistrationForm> b)
{
    b.Property(x => x.Username)
        .Required()
        .MinLength(3)
        .MaxLength(20)
        .Match(@"^[a-zA-Z0-9_]+$", "Username can only contain letters, numbers, and underscores");

    b.Property(x => x.Email)
        .Required()
        .Email();

    b.Property(x => x.Password)
        .Required()
        .MinLength(8);
}
```

This is equivalent to stacking attributes but allows dynamic composition.

### Cross-field comparisons

Use comparison methods on `b.Property()` to compare one property against another:

```csharp
public void Configure(IValidationBuilder<DateRange> b)
{
    b.Property(x => x.EndDate).GreaterThan(x => x.StartDate);
    b.Property(x => x.Max).GreaterThanOrEqual(x => x.Min);
    b.Property(x => x.ConfirmPassword).EqualTo(x => x.Password, "Passwords must match");
}
```

Available comparisons:

| Method | Description |
|--------|-------------|
| `.GreaterThan(x => x.Other)` | Property must be greater than another |
| `.GreaterThanOrEqual(x => x.Other)` | Property must be greater than or equal to another |
| `.LessThan(x => x.Other)` | Property must be less than another |
| `.LessThanOrEqual(x => x.Other)` | Property must be less than or equal to another |
| `.EqualTo(x => x.Other)` | Property must equal another |
| `.NotEqualTo(x => x.Other)` | Property must not equal another |

All accept an optional custom message as the second argument.

### Conditional validation (When / Unless)

Use `b.When()` to apply rules only when a condition is met:

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

When the condition is `false`, all inner rules are skipped entirely.

Use `b.Unless()` for the inverse — rules apply when the condition is `false`:

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

### RuleSet

Group rules under named sets and validate selectively:

```csharp
[ZValidate]
public partial class UserDto : IValidationConfigurator<UserDto>
{
    [ZRequired] public string Name { get; set; } = "";
    [ZRequired] [ZEmail] public string Email { get; set; } = "";
    [ZMinLength(8)] public string Password { get; set; } = "";

    public void Configure(IValidationBuilder<UserDto> b)
    {
        b.RuleSet("Create", set =>
        {
            set.Property(x => x.Password).Required().MinLength(8);
        });

        b.RuleSet("Update", set =>
        {
            set.Rule(x => x.Name.Length > 0 || x.Email.Length > 0,
                "At least one field must be provided for update");
        });
    }
}
```

Invoke a specific rule set:

```csharp
// Validates all attribute rules + "Create" rule set
var result = user.Validate(context: null, ruleSet: "Create");

// Validates only attribute rules (no rule sets)
var result = user.Validate();
```

## CascadeMode

By default, all rules for a property are evaluated and all failures are returned. Add `[ZCascade]` to stop after the first failure for that property:

```csharp
[ZValidate]
public partial class LoginRequest
{
    [ZCascade]
    [ZRequired]
    [ZEmail]
    [ZMaxLength(255)]
    public string Email { get; set; } = "";
}
```

If `Email` is empty, only `"Email is required."` is returned — the email format and max-length checks are skipped. This is useful when later rules only make sense if earlier ones pass.

## Message Placeholders

Custom error messages support these placeholders:

| Placeholder | Replaced with |
|-------------|---------------|
| `{PropertyName}` | The name of the property being validated |
| `{PropertyValue}` | The current value of the property |

```csharp
[ZRange(1, 100, Message = "{PropertyName} must be 1-100, but was {PropertyValue}")]
public int Quantity { get; set; }

// Error: "Quantity must be 1-100, but was 250"
```

Placeholders work in all attribute-based messages. For fluent `b.Rule()` messages, use string interpolation in the lambda message or hardcoded strings since the message is a compile-time constant.

## Nested Validation

### Objects

Properties whose type is also marked `[ZValidate]` are automatically validated. Errors include the full property path:

```csharp
[ZValidate]
public partial class Address
{
    [ZRequired] public string Street { get; set; } = "";
    [ZRequired] public string City { get; set; } = "";
    [ZRequired] [ZMatch(@"^\d{5}$")] public string Zip { get; set; } = "";
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
    Name = "Alice",
    BillingAddress = new Address { Street = "", City = "NYC", Zip = "abc" },
    ShippingAddress = null  // not validated
};

var result = form.Validate();
// ValidationErrors:
//   Property="BillingAddress.Street"  Message="Street is required."
//   Property="BillingAddress.Zip"     Message="Zip must match pattern ^\d{5}$."
```

Nullable nested properties are skipped when `null`.

### Collections

Collections of `[ZValidate]` types are validated element-by-element. The error path includes the index:

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
        new LineItem { Sku = "", Quantity = 5 },
        new LineItem { Sku = "ABC", Quantity = 0 },
    }
};

var result = invoice.Validate();
// "Lines[0].Sku"      → "Sku is required."
// "Lines[1].Quantity"  → "Quantity must be between 1 and 9999."
```

### ValidationContext

A `ValidationContext` is automatically created at the root and flows through nested validators. You only pass one explicitly if you need custom data:

```csharp
// Auto — context created internally with RootObject = form
var result = form.Validate();

// Manual — pass custom data via Items
var result = form.Validate(new ValidationContext
{
    Items = { ["tenant"] = "acme", ["userId"] = currentUserId },
});
```

| Property | Description |
|----------|-------------|
| `Path` | Dot-separated path from root (e.g. `"BillingAddress"`, `"Lines[0]"`) — auto-set |
| `Parent` | The object that triggered this nested validation — auto-set |
| `RootObject` | The top-level object that started the chain — auto-set |
| `Items` | `Dictionary<string, object?>` for custom user data |

## ValidationResult & ValidationError

```csharp
public sealed class ValidationResult
{
    /// True if no errors
    public bool IsValid { get; }

    /// Structured errors — each has Property path + Message
    public IReadOnlyList<ValidationError> ValidationErrors { get; }

    /// Flat error messages (backward compat)
    public IReadOnlyList<string> Errors { get; }

    /// Group errors by property — matches ASP.NET ModelState shape
    public Dictionary<string, List<string>> ToDictionary();

    /// Singleton success instance
    public static readonly ValidationResult Success;
}

public sealed class ValidationError
{
    /// Property path from root (e.g. "BillingAddress.Street", "Lines[0].Sku")
    public string Property { get; }

    /// Human-readable message (e.g. "Street is required.")
    public string Message { get; }

    /// Combined: "BillingAddress.Street: Street is required."
    public string FullMessage { get; }
}
```

Usage patterns:

```csharp
var result = form.Validate();

// Iterate structured errors
foreach (var err in result.ValidationErrors)
    Console.WriteLine($"[{err.Property}] {err.Message}");

// Get ModelState-shaped dictionary for API responses
var dict = result.ToDictionary();
// {
//   "Email": ["Email is required."],
//   "BillingAddress.Street": ["Street is required."]
// }

// Quick flat list
foreach (var msg in result.Errors)
    Console.WriteLine(msg);
```

## ASP.NET Core Integration

### Minimal API — .WithValidation() endpoint filter

The recommended approach for Minimal APIs. Any parameter implementing `IValidatable` is automatically validated before your handler executes:

```csharp
app.MapPost("/api/users", (CreateUserRequest req) =>
{
    // Only reached if req.Validate().IsValid == true
    return Results.Ok(CreateUser(req));
})
.WithValidation();
```

On validation failure, returns HTTP 400 with an RFC 7807 `ValidationProblem` response:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["Email is required.", "Email must be a valid email address."],
    "Age": ["Age must be between 18 and 120."]
  }
}
```

### Route groups

Apply `.WithValidation()` to an entire route group — all endpoints in the group get automatic validation:

```csharp
var api = app.MapGroup("/api").WithValidation();

api.MapPost("/users", (CreateUserRequest req) => /* ... */);
api.MapPost("/orders", (OrderRequest req) => /* ... */);
api.MapPut("/users/{id}", (UpdateUserRequest req) => /* ... */);
// All three endpoints auto-validate their IValidatable parameters
```

### Controllers (manual)

For MVC controllers, call `Validate()` manually:

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public IActionResult Create(CreateUserRequest request)
    {
        var validation = request.Validate();
        if (!validation.IsValid)
            return ValidationProblem(
                new ValidationProblemDetails(validation.ToDictionary()));

        return Ok(CreateUser(request));
    }
}
```

## What Gets Generated

For a class like:

```csharp
[ZValidate]
public partial class CreateUserRequest
{
    [ZRequired]
    [ZMinLength(2)]
    public string Name { get; set; } = "";

    [ZRequired]
    [ZEmail]
    public string Email { get; set; } = "";

    [ZRange(18, 120)]
    public int Age { get; set; }
}
```

The source generator emits approximately:

```csharp
// <auto-generated/>
public partial class CreateUserRequest : IValidatable
{
    public ValidationResult Validate(ValidationContext? context = null)
    {
        context ??= new ValidationContext { RootObject = this };
        var errors = new List<ValidationError>();

        // Name: [ZRequired]
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add(new ValidationError(
                context.BuildPath("Name"), "Name is required."));
        }

        // Name: [ZMinLength(2)]
        if (Name is not null && Name.Length < 2)
        {
            errors.Add(new ValidationError(
                context.BuildPath("Name"), "Name must be at least 2 characters."));
        }

        // Email: [ZRequired]
        if (string.IsNullOrWhiteSpace(Email))
        {
            errors.Add(new ValidationError(
                context.BuildPath("Email"), "Email is required."));
        }

        // Email: [ZEmail]
        if (Email is not null && !System.Text.RegularExpressions.Regex.IsMatch(
            Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            errors.Add(new ValidationError(
                context.BuildPath("Email"), "Email must be a valid email address."));
        }

        // Age: [ZRange(18, 120)]
        if (Age < 18 || Age > 120)
        {
            errors.Add(new ValidationError(
                context.BuildPath("Age"), "Age must be between 18 and 120."));
        }

        return errors.Count == 0
            ? ValidationResult.Success
            : new ValidationResult(errors);
    }
}
```

Key observations:
- No reflection, no expression trees, no runtime code generation
- Null checks guard length/format validators (null values pass unless `[ZRequired]` is also present)
- Context path is built for nested validation support
- Returns the singleton `ValidationResult.Success` on the happy path (zero allocations)

## Comparison with FluentValidation

| Feature | ZibStack.NET.Validation | FluentValidation |
|---------|------------------------|------------------|
| Approach | Source-generated at compile time | Runtime reflection |
| Runtime overhead | None (zero reflection) | Expression compilation + reflection |
| Startup cost | None | Validator discovery + registration |
| AOT / trimming | Fully compatible | Partial (reflection issues) |
| Attribute-based rules | Yes (primary API) | No |
| Fluent rules | Yes (`IValidationConfigurator<T>`) | Yes (primary API) |
| Cross-field validation | Yes | Yes |
| Conditional rules | `When` / `Unless` | `When` / `Unless` |
| Async rules | Not yet | Yes |
| Custom validators | `b.Rule(x => ...)` | `Must()` / custom classes |
| Nested validation | Auto-detected | Manual `.SetValidator()` |
| Collection validation | Auto with index path | `RuleForEach` |
| CascadeMode | Per-property `[ZCascade]` | Per-property / global |
| RuleSet | Yes | Yes |
| DI integration | Not needed (no validator classes) | Built-in |
| ASP.NET integration | `.WithValidation()` filter | FluentValidation.AspNetCore |
| NuGet size | Tiny (analyzer only) | ~300 KB runtime |

## Records Support

Works with `record` and `record struct` types — just add `partial`:

```csharp
[ZValidate]
public partial record ProductRequest
{
    [ZRequired]
    public string Sku { get; init; } = "";

    [ZRange(0.01, 999999.99)]
    public decimal Price { get; init; }

    [ZMaxLength(500)]
    public string? Description { get; init; }
}

[ZValidate]
public partial record struct Coordinate
{
    [ZRange(-90, 90)] public double Latitude { get; init; }
    [ZRange(-180, 180)] public double Longitude { get; init; }
}
```

```csharp
var product = new ProductRequest { Sku = "", Price = -5 };
var result = product.Validate();
// Sku: "Sku is required."
// Price: "Price must be between 0.01 and 999999.99."
```

## Requirements

- .NET 8.0+
- Types must be declared `partial` (required for source generators)
- The package is an analyzer — it produces code at compile time and adds zero runtime DLLs to your output

## License

MIT
