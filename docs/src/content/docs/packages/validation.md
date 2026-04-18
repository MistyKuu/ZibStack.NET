---
title: ZibStack.NET.Validation
description: Source-generated validation for .NET — add validation attributes, get a compile-time Validate() method with no reflection or runtime overhead.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Validation.svg)](https://www.nuget.org/packages/ZibStack.NET.Validation) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Validation)

Source-generated validation for .NET. Add validation attributes, get a compile-time `Validate()` method — no reflection, no runtime overhead.

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

The generator creates a `Validate()` method at compile-time:

```csharp
var request = new CreateUserRequest { Name = "", Email = "bad", Age = 10 };
var result = request.Validate();

if (!result.IsValid)
{
    foreach (var error in result.Errors)
        Console.WriteLine(error);
    // "Name is required."
    // "Email must be a valid email address."
    // "Age must be between 18 and 120."
}
```

## Validation Attributes

| Attribute | Target | Description |
|-----------|--------|-------------|
| `[ZValidate]` | Class/Record | Marks the type for validation generation (must be `partial`) |
| `[ZRequired]` | Property | Must not be null (or empty/whitespace for strings) |
| `[ZMinLength(n)]` | Property | String/collection must have at least `n` characters/items |
| `[ZMaxLength(n)]` | Property | String/collection must have at most `n` characters/items |
| `[ZRange(min, max)]` | Property | Numeric value must be within range (inclusive) |
| `[ZEmail]` | Property | Must be a valid email address |
| `[ZUrl]` | Property | Must be a valid absolute URL |
| `[ZMatch(pattern)]` | Property | Must match the regex pattern |
| `[ZNotEmpty]` | Property | Collection must have items; string must not be whitespace |

All attributes support a `Message` property for custom error messages:

```csharp
[ZRequired(Message = "Please provide your name")]
[ZMatch(@"^\d{3}-\d{4}$", Message = "Phone must be in format XXX-XXXX")]
```

## Cross-field Validation

Implement `IValidationConfigurator<T>` to add rules that compare properties against each other. The generator parses `Configure()` at compile time — it's never invoked at runtime.

### Free-form expressions

```csharp
[ZValidate]
public partial class DateRange : IValidationConfigurator<DateRange>
{
    [ZRequired] public DateTime StartDate { get; set; }
    [ZRequired] public DateTime EndDate { get; set; }

    public void Configure(IValidationBuilder<DateRange> b)
    {
        b.Rule(x => x.EndDate > x.StartDate, "EndDate must be after StartDate");
    }
}
```

### Per-property comparisons

```csharp
[ZValidate]
public partial class PasswordForm : IValidationConfigurator<PasswordForm>
{
    [ZRequired] [ZMinLength(8)]
    public string Password { get; set; } = "";
    [ZRequired]
    public string ConfirmPassword { get; set; } = "";

    public void Configure(IValidationBuilder<PasswordForm> b)
    {
        b.Property(x => x.ConfirmPassword).EqualTo(x => x.Password, "Passwords must match");
    }
}
```

Available comparisons: `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `EqualTo`, `NotEqualTo`. Cross-field rules combine with per-property attributes — both are checked in the generated `Validate()` method.

## IValidatable Interface

All `[ZValidate]` types implement `IValidatable`:

```csharp
public interface IValidatable
{
    ValidationResult Validate();
}

// Use in generic code
void Process<T>(T request) where T : IValidatable
{
    var result = request.Validate();
    if (!result.IsValid)
        throw new ArgumentException(string.Join(", ", result.Errors));
}
```

## ValidationResult

```csharp
public sealed class ValidationResult
{
    public IReadOnlyList<string> Errors { get; }
    public bool IsValid { get; }

    public static readonly ValidationResult Success; // singleton
}
```

## Records

Works with records too:

```csharp
[ZValidate]
public partial record ProductRequest
{
    [ZRequired]
    public string Sku { get; init; } = "";

    [ZRange(0, 999999)]
    public decimal Price { get; init; }
}
```

## ASP.NET Core Integration

```csharp
[HttpPost]
public IActionResult Create(CreateUserRequest request)
{
    var validation = request.Validate();
    if (!validation.IsValid)
        return BadRequest(new { Errors = validation.Errors });

    // proceed...
}
```

## Requirements

- .NET 8.0+
- Types must be `partial`

## License

MIT
