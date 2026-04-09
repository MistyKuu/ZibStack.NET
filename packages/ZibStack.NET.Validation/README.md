# ZibStack.NET.Validation

Source-generated validation for .NET — add validation attributes, get a compile-time `Validate()` method with no reflection.

## Install

```
dotnet add package ZibStack.NET.Validation
```

## Quick Start

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

var result = new CreateUserRequest { Name = "", Email = "bad", Age = 10 }.Validate();
// result.Errors: "Name is required.", "Email must be a valid email address.", ...
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/validation/](https://mistykuu.github.io/ZibStack.NET/packages/validation/)
