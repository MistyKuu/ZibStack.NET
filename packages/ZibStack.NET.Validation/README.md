# ZibStack.NET.Validation

Source-generated validation for .NET. Add validation attributes, get a compile-time `Validate()` method — no reflection, no runtime overhead.

## Install

```bash
dotnet add package ZibStack.NET.Validation
```

## Quick Start

```csharp
using ZibStack.NET.Validation;

[Validate]
public partial class CreateUserRequest
{
    [Required]
    [MinLength(2)]
    [MaxLength(50)]
    public string Name { get; set; } = "";

    [Required]
    [Email]
    public string Email { get; set; } = "";

    [Range(18, 120)]
    public int Age { get; set; }

    [Url]
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
| `[Validate]` | Class/Record | Marks the type for validation generation (must be `partial`) |
| `[Required]` | Property | Must not be null (or empty/whitespace for strings) |
| `[MinLength(n)]` | Property | String/collection must have at least `n` characters/items |
| `[MaxLength(n)]` | Property | String/collection must have at most `n` characters/items |
| `[Range(min, max)]` | Property | Numeric value must be within range (inclusive) |
| `[Email]` | Property | Must be a valid email address |
| `[Url]` | Property | Must be a valid absolute URL |
| `[Match(pattern)]` | Property | Must match the regex pattern |
| `[NotEmpty]` | Property | Collection must have items; string must not be whitespace |

All attributes support a `Message` property for custom error messages:

```csharp
[Required(Message = "Please provide your name")]
[Match(@"^\d{3}-\d{4}$", Message = "Phone must be in format XXX-XXXX")]
```

## IValidatable Interface

All `[Validate]` types implement `IValidatable`:

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
[Validate]
public partial record ProductRequest
{
    [Required]
    public string Sku { get; init; } = "";

    [Range(0, 999999)]
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
