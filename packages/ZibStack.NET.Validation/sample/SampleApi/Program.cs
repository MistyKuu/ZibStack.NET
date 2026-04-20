using SampleApi.Models;
using ZibStack.NET.Validation;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ── Orders (auto-validation via .WithValidation()) ──────────────────────────

var orders = app.MapGroup("/api/orders").WithValidation();

orders.MapPost("/", (CreateOrderRequest req) =>
{
    // Only reached if ALL validation passes (nested, conditional, collections)
    var total = req.Items.Sum(i => i.UnitPrice * i.Quantity - i.Discount);
    total -= total * req.DiscountPercent / 100;

    return Results.Created("/api/orders/1", new
    {
        Id = 1,
        Customer = req.CustomerName,
        Items = req.Items.Count,
        Total = total,
        Shipping = req.ShippingMethod,
    });
});

orders.MapPost("/validate-express", (CreateOrderRequest req) =>
{
    // Validate with specific RuleSet
    var result = req.Validate(null, "Express");
    if (!result.IsValid)
        return Results.BadRequest(result.ToDictionary());
    return Results.Ok("Express validation passed");
});

// ── Users (auto-validation) ─────────────────────────────────────────────────

app.MapPost("/api/register", (RegisterUserRequest req) =>
{
    return Results.Ok(new { Username = req.Username, Plan = req.Plan });
})
.WithValidation();

// ── Demo endpoints: show validation errors ────────���─────────────────────────

app.MapGet("/api/demo/order-errors", () =>
{
    var badOrder = new CreateOrderRequest
    {
        CustomerName = "",
        CustomerEmail = "not-an-email",
        ShippingMethod = "express",
        DiscountPercent = 50, // requires coupon
        Payment = new PaymentInfo
        {
            CardType = "invalid",
            CardNumber = "1234",
            Expiry = "bad",
            Cvv = 1,
        },
        BillingAddress = new Address { Street = "", City = "", Zip = "bad" },
        // No items → ZNotEmpty fires
        // No shipping address → conditional fires (express)
        // No coupon → business rule fires (discount requires coupon)
    };

    var result = badOrder.Validate();
    return Results.Ok(new
    {
        IsValid = result.IsValid,
        ErrorCount = result.ValidationErrors.Count,
        Errors = result.ValidationErrors.Select(e => new { e.Property, e.Message }),
        Grouped = result.ToDictionary(),
    });
});

app.MapGet("/api/demo/register-errors", () =>
{
    var badRegister = new RegisterUserRequest
    {
        Username = "",           // [ZCascade] → only first error ("cannot be empty")
        Email = "bad-email",     // placeholder → "'bad-email' is not a valid email"
        Password = "12",         // [ZCascade] → "at least 8 chars" only
        ConfirmPassword = "99",  // cross-field → "Passwords must match"
        Age = 5,                 // range → "must be between 13-120"
        Plan = "enterprise",     // conditional → "Enterprise plan requires a website"
    };

    var result = badRegister.Validate();
    return Results.Ok(new
    {
        IsValid = result.IsValid,
        ErrorCount = result.ValidationErrors.Count,
        Errors = result.ValidationErrors.Select(e => new { e.Property, e.Message }),
    });
});

app.Run();
