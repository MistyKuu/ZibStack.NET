# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, DTOs, and more.

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides interpolated string logging (`LogInformationEx($"...")`). |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for strongly-typed Create and Update request DTOs with PatchField support. |

## Quick Examples

### ZibStack.NET.Log

```csharp
[ZibLog]
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    [Log]
    public Order PlaceOrder(int customerId, [Sensitive] string creditCard)
    {
        return _repo.Create(customerId, creditCard);
    }
}
// log: Entering OrderService.PlaceOrder(customerId: 42, creditCard: ***)
// log: Exited OrderService.PlaceOrder in 53ms -> {"Id":1,"Product":"Widget"}

// Interpolated string logging:
_logger.LogInformationEx($"User {userId} bought {product} for {total:C}");
```

### ZibStack.NET.Dto

```csharp
[DtoFor(typeof(Player), Generate.Create | Generate.Update)]
public partial class PlayerDto { }

// Generates: CreatePlayerDto, UpdatePlayerDto with PatchField<T> support
```

## Repository Structure

```
ZibStack.NET/
├── packages/
│   ├── ZibStack.NET.Log/          → Logging source generator
│   │   ├── src/                   → Generator + Abstractions
│   │   ├── tests/                 → Unit tests + Benchmarks
│   │   └── sample/                → Sample API
│   └── ZibStack.NET.Dto/          → DTO source generator
│       ├── src/                   → Generator
│       ├── tests/                 → Unit tests
│       └── sample/                → Sample API
├── .github/workflows/
│   ├── ci.yml                     → Builds & tests all packages
│   ├── release-log.yml            → Release ZibStack.NET.Log to NuGet
│   └── release-dto.yml            → Release ZibStack.NET.Dto to NuGet
└── ZibStack.NET.slnx
```

## License

MIT
