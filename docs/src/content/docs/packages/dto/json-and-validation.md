---
title: JSON serializer & custom validation
description: PatchField JSON converter registration (System.Text.Json + Newtonsoft) and how to plug in your own validator (manual or FluentValidation).
---

## JSON serializer support

The generator detects which serializers are available and produces the corresponding converters:

| Serializer | Generated converter | Registration |
|---|---|---|
| System.Text.Json | `PatchFieldJsonConverterFactory` | `options.Converters.Add(new PatchFieldJsonConverterFactory())` |
| Newtonsoft.Json | `PatchFieldNewtonsoftConverter` | `settings.Converters.Add(new PatchFieldNewtonsoftConverter())` |

Both are generated if both packages are referenced. No converter is auto-registered -- you choose which one to use, or write your own.

## Custom validation

### Simple validator

Implement `IDtoValidator<T>` and point to it from the attribute:

```csharp
public class MyCreateValidator : IDtoValidator<CreatePlayerRequest>
{
    public DtoValidationResult Validate(CreatePlayerRequest instance)
    {
        var result = new DtoValidationResult();
        if (instance.Name.HasValue && instance.Name.Value.Length < 3)
            result.AddError("name", "must be at least 3 characters.");
        return result;
    }
}

[CreateDto(Validator = typeof(MyCreateValidator))]
public class Player { ... }
```

When a validator is set, `Validate()` delegates entirely to it -- the default generated rules are replaced.

### FluentValidation

When FluentValidation is installed, the generator additionally produces:

- `FluentDtoValidator<T>` -- base class bridging FluentValidation with `IDtoValidator<T>`
- `{RequestName}CreateBaseValidator` -- contains the generated required/null rules
- `{RequestName}UpdateBaseValidator` -- contains the generated null rules

Inherit to extend:

```csharp
public class MyCreateValidator : CreatePlayerRequestCreateBaseValidator
{
    public MyCreateValidator()
    {
        RuleFor(x => x.Name)
            .Must(f => !f.HasValue || f.Value.Length >= 3)
            .WithMessage("Name must be at least 3 characters.");
    }
}

[CreateDto(Validator = typeof(MyCreateValidator))]
public class Player { ... }
```

Or start from scratch:

```csharp
public class MyCreateValidator : FluentDtoValidator<CreatePlayerRequest>
{
    public MyCreateValidator()
    {
        // your rules only
    }
}
```

## Related guides

- [Full CRUD with SQLite](/ZibStack.NET/guides/crud-sqlite/) — end-to-end project with `[CrudApi]`, relationships, Query DSL, observability, and PatchField tri-state demo
- [Modeling Relationships & Query DSL](/ZibStack.NET/guides/relationships-query-dsl/) — `[OneToOne]` / `[OneToMany]`, dot-notation filtering, every DSL operator with SQL translations
- [PatchField Tri-State](/ZibStack.NET/guides/patchfield-tri-state/) — the null-vs-missing problem and `PatchField<T>` with pattern-matching handlers
- [Declarative Observability](/ZibStack.NET/guides/observability/) — `[Log]` + `[Trace]` for structured logging and OpenTelemetry spans on CRUD stores

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects and System.Text.Json NuGet)
- `required` keyword needs C# 11 / .NET 7+ (optional — without it all properties are optional in Create)

## License

MIT
