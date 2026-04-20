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

// ── Projects (deep nesting demo) ────────────────────────────────────────────

app.MapPost("/api/projects", (CreateProjectRequest req) =>
{
    return Results.Created("/api/projects/1", new { Name = req.Name, Tasks = req.Tasks.Count });
})
.WithValidation();

app.MapGet("/api/demo/project-errors", () =>
{
    var badProject = new CreateProjectRequest
    {
        Name = "",  // cascade
        RepositoryUrl = "not-a-url",
        Status = "active",  // triggers conditional: needs task in progress + milestone
        Budget = 1000,
        SpentSoFar = 5000,  // exceeds budget
        MaxTeamSize = 3,
        TeamEmails = new() { "valid@example.com", "not-an-email", "also@bad", "extra@one.com" }, // exceeds max
        Tasks = new()
        {
            new TaskItem
            {
                Title = "",  // required
                Status = "done",  // conditional: all subtasks must be done
                AssigneeEmail = "bad",
                Tags = new(), // ZNotEmpty
                Subtasks = new()
                {
                    new Subtask
                    {
                        Title = "",  // required
                        Status = "done",  // conditional: must have 100% + actual hours
                        ProgressPercent = 50,
                    },
                    new Subtask
                    {
                        Title = "Valid subtask",
                        Status = "doing",
                        EstimatedHours = 0,  // Unless todo: needs estimate
                    }
                }
            }
        },
        Labels = new()
        {
            new Tag { Name = "INVALID CAPS!", Priority = "unknown" },
            new Tag { Name = "valid-tag", Priority = "high" },
        }
    };

    var result = badProject.Validate();
    return Results.Ok(new
    {
        IsValid = result.IsValid,
        ErrorCount = result.ValidationErrors.Count,
        Errors = result.ValidationErrors.Select(e => new { e.Property, e.Message }),
        Grouped = result.ToDictionary(),
    });
});

app.Run();
