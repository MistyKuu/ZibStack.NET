---
title: ASP.NET Integration
description: Automatic validation for Minimal APIs with .WithValidation() endpoint filter and manual controller integration.
---

## Minimal API — `.WithValidation()` (recommended)

Add `.WithValidation()` to any endpoint — parameters implementing `IValidatable` are auto-validated before your handler runs:

```csharp
app.MapPost("/api/users", (CreateUserRequest req) =>
{
    // Only reached if req.Validate().IsValid == true
    return Results.Created($"/api/users/{user.Id}", user);
})
.WithValidation();
```

Returns `400 ValidationProblem` (RFC 7807) when validation fails:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Name": ["Name is required.", "Name must be at least 2 characters."],
    "Email": ["Email must be a valid email address."],
    "Age": ["Age must be between 18 and 120."]
  }
}
```

## Route Groups

Apply validation to ALL endpoints in a group:

```csharp
var api = app.MapGroup("/api").WithValidation();

api.MapPost("/users", (CreateUserRequest req) => ...);
api.MapPut("/users/{id}", (UpdateUserRequest req) => ...);
api.MapPost("/orders", (OrderRequest req) => ...);
// All three auto-validate their IValidatable parameters
```

## Multiple Parameters

If an endpoint has multiple `IValidatable` parameters, all are validated. First failure wins:

```csharp
app.MapPost("/api/transfer", (SourceAccount src, TargetAccount dst) => ...)
    .WithValidation();
// Both src and dst are validated; first invalid returns 400
```

## Controllers (manual)

For MVC controllers, call `Validate()` explicitly:

```csharp
[HttpPost]
public IActionResult Create(CreateUserRequest request)
{
    var result = request.Validate();
    if (!result.IsValid)
        return ValidationProblem(
            new ValidationProblemDetails(
                result.ToDictionary()
                    .ToDictionary(kv => kv.Key, kv => kv.Value.ToArray())));

    // proceed...
}
```

## With RuleSet

```csharp
app.MapPost("/api/users", (UserDto dto) =>
{
    var result = dto.Validate(null, "Create");
    if (!result.IsValid)
        return Results.ValidationProblem(
            result.ToDictionary().ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
    // ...
});
```

> Note: `.WithValidation()` calls `Validate()` with no ruleset (validates all). For selective validation, call manually in the handler.

## Localization (i18n)

Register an `IValidationLocalizer` in DI — all generated messages pass through it automatically:

```csharp
// 1. Implement the interface:
public class PolishLocalizer : IValidationLocalizer
{
    private readonly IStringLocalizer<SharedResources> _loc;
    
    public PolishLocalizer(IStringLocalizer<SharedResources> loc) => _loc = loc;

    public string? GetMessage(string property, string defaultMessage)
    {
        var localized = _loc[defaultMessage];
        return localized.ResourceNotFound ? null : localized.Value;
    }
}

// 2. Register in DI:
builder.Services.AddSingleton<IValidationLocalizer, PolishLocalizer>();

// 3. Call once at startup:
app.Services.ConfigureValidation();
```

That's it. `.WithValidation()` auto-resolves the localizer. Manual `Validate()` also uses it (resolved from the static `ValidationServiceProvider`).

Without `IValidationLocalizer` in DI → default English messages (zero breaking change).

## Setup

```csharp
var app = builder.Build();
app.Services.ConfigureValidation();  // bridges DI → validation runtime (for localization)
app.Services.ConfigureAop();         // if using [Log]/[Trace] etc.
```

## Requirements

`.WithValidation()` is auto-generated when your project references `Microsoft.AspNetCore.Http` (Web SDK projects). Non-web projects (class libraries, test projects) don't get this extension method — it's conditionally emitted.
