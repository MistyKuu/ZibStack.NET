using ZibStack.NET.Validation;

namespace SampleApi.Models;

// ══════════════════════════════════════════════════════════════════════════════
// E-commerce order — demonstrates ALL validation features in one model tree
// ══════════════════════════════════════════════════════════════════════════════

// ── Address (nested, reused in multiple places) ─────────────────────────────

[ZValidate]
public partial class Address
{
    [ZRequired]
    [ZMinLength(3)]
    public string Street { get; set; } = "";

    [ZRequired]
    public string City { get; set; } = "";

    [ZRequired]
    [ZMatch(@"^\d{5}(-\d{4})?$", Message = "Zip must be 5 digits (or 5+4 format)")]
    public string Zip { get; set; } = "";

    [ZPhone]
    public string? Phone { get; set; }
}

// ── Payment info ────────────────────────────────────────────────────────────

[ZValidate]
public partial class PaymentInfo
{
    [ZRequired]
    [ZIn("visa", "mastercard", "amex", "discover")]
    public string CardType { get; set; } = "";

    [ZRequired]
    [ZCreditCard]
    public string CardNumber { get; set; } = "";

    [ZRequired]
    [ZMatch(@"^\d{2}/\d{2}$", Message = "Expiry must be MM/YY format")]
    public string Expiry { get; set; } = "";

    [ZRange(100, 9999)]
    public int Cvv { get; set; }
}

// ── Line item (in collection) ───────────────────────────────────────────────

[ZValidate]
public partial class OrderItem : IValidationConfigurator<OrderItem>
{
    [ZRequired]
    [ZMinLength(3)]
    [ZMaxLength(50)]
    public string Sku { get; set; } = "";

    [ZRequired]
    public string ProductName { get; set; } = "";

    [ZRange(1, 999)]
    public int Quantity { get; set; }

    [ZRange(0.01, 99999.99)]
    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    public void Configure(IValidationBuilder<OrderItem> b)
    {
        b.Rule(x => x.Discount >= 0 && x.Discount <= x.UnitPrice * x.Quantity,
            "Discount cannot exceed item total");
    }
}

// ── Main order request (the big one) ────────────────────────────────────────

[ZValidate]
public partial class CreateOrderRequest : IValidationConfigurator<CreateOrderRequest>
{
    // ── Customer info ──
    [ZRequired]
    [ZMinLength(2)]
    [ZMaxLength(100)]
    public string CustomerName { get; set; } = "";

    [ZRequired]
    [ZEmail]
    public string CustomerEmail { get; set; } = "";

    [ZNotIn("test@test.com", "admin@example.com")]
    public string? NotificationEmail { get; set; }

    // ── Nested objects ──
    public Address BillingAddress { get; set; } = new();
    public Address? ShippingAddress { get; set; }  // null = same as billing

    // ── Nested collection ──
    [ZNotEmpty]
    public List<OrderItem> Items { get; set; } = new();

    // ── Payment ──
    public PaymentInfo? Payment { get; set; }  // null for free orders

    // ── Order metadata ──
    [ZIn("standard", "express", "overnight")]
    public string ShippingMethod { get; set; } = "standard";

    [ZRange(0, 100)]
    public decimal DiscountPercent { get; set; }

    public string? CouponCode { get; set; }
    public bool IsFreeOrder { get; set; }

    [ZMaxLength(500)]
    public string? Notes { get; set; }

    // ── Cross-field + conditional rules ──
    public void Configure(IValidationBuilder<CreateOrderRequest> b)
    {
        // Free orders don't need payment
        b.Unless(x => x.IsFreeOrder, then =>
        {
            then.Rule(x => x.Payment != null, "Payment is required for non-free orders");
        });

        // Express/overnight need shipping address
        b.When(x => x.ShippingMethod != "standard", then =>
        {
            then.Rule(x => x.ShippingAddress != null,
                "Shipping address required for express/overnight delivery");
        });

        // Coupon code format
        b.When(x => x.CouponCode != null, then =>
        {
            then.Rule(x => x.CouponCode!.Length >= 5 && x.CouponCode.Length <= 20,
                "Coupon code must be 5-20 characters");
            then.Rule(x => x.CouponCode!.All(char.IsLetterOrDigit),
                "Coupon code must be alphanumeric");
        });

        // Business rules
        b.Rule(x => x.Items.Count <= 50, "Maximum 50 items per order");
        b.Rule(x => x.DiscountPercent == 0 || x.CouponCode != null,
            "Discount requires a coupon code");

        // RuleSets for different operations
        b.RuleSet("Express", set =>
        {
            set.Rule(x => x.Items.Count <= 10, "Express orders limited to 10 items");
            set.Rule(x => x.ShippingAddress != null, "Express requires shipping address");
        });
    }
}

// ── Registration (cascade + placeholders demo) ──────────────────────────────

[ZValidate]
public partial class RegisterUserRequest : IValidationConfigurator<RegisterUserRequest>
{
    [ZRequired(Message = "{PropertyName} cannot be empty")]
    [ZMinLength(3, Message = "{PropertyName} must be at least 3 chars")]
    [ZMaxLength(30, Message = "{PropertyName} too long (max 30)")]
    [ZCascade]
    public string Username { get; set; } = "";

    [ZRequired]
    [ZEmail(Message = "'{PropertyValue}' is not a valid email")]
    public string Email { get; set; } = "";

    [ZRequired]
    [ZMinLength(8)]
    [ZCascade]
    public string Password { get; set; } = "";

    [ZRequired]
    public string ConfirmPassword { get; set; } = "";

    [ZRange(13, 120, Message = "You must be between {PropertyName} 13-120")]
    public int Age { get; set; }

    [ZUrl]
    public string? Website { get; set; }

    [ZIn("free", "pro", "enterprise")]
    public string Plan { get; set; } = "free";

    public void Configure(IValidationBuilder<RegisterUserRequest> b)
    {
        b.Property(x => x.ConfirmPassword)
            .EqualTo(x => x.Password, "Passwords must match");

        b.When(x => x.Plan == "enterprise", then =>
        {
            then.Rule(x => x.Website != null, "Enterprise plan requires a website");
        });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Project management — deep nesting, collections of collections, cross-refs
// ═══════════════════════════════════════════════════════════════════��══════════

// ── Tag (simple nested in collection) ───────────────────────────────────────

[ZValidate]
public partial class Tag
{
    [ZRequired]
    [ZMinLength(1)]
    [ZMaxLength(30)]
    [ZMatch(@"^[a-z0-9\-]+$", Message = "Tags must be lowercase alphanumeric with dashes")]
    public string Name { get; set; } = "";

    [ZIn("low", "medium", "high", "critical")]
    public string Priority { get; set; } = "medium";
}

// ── Subtask (nested in Task, which is nested in Project) ────────────────────

[ZValidate]
public partial class Subtask : IValidationConfigurator<Subtask>
{
    [ZRequired]
    [ZMaxLength(200)]
    public string Title { get; set; } = "";

    [ZRange(0, 100)]
    public int ProgressPercent { get; set; }

    [ZIn("todo", "doing", "done", "blocked")]
    public string Status { get; set; } = "todo";

    public int EstimatedHours { get; set; }
    public int ActualHours { get; set; }

    public void Configure(IValidationBuilder<Subtask> b)
    {
        b.When(x => x.Status == "done", then =>
        {
            then.Rule(x => x.ProgressPercent == 100,
                "Completed subtask must have 100% progress");
            then.Rule(x => x.ActualHours > 0,
                "Completed subtask must have actual hours logged");
        });

        b.Unless(x => x.Status == "todo", then =>
        {
            then.Rule(x => x.EstimatedHours > 0,
                "Started subtask must have estimated hours");
        });
    }
}

// ── TaskItem (nested in Project, contains collection of Subtasks) ───────────

[ZValidate]
public partial class TaskItem : IValidationConfigurator<TaskItem>
{
    [ZRequired]
    [ZMinLength(3)]
    [ZMaxLength(200)]
    public string Title { get; set; } = "";

    [ZMaxLength(2000)]
    public string? Description { get; set; }

    [ZRequired]
    [ZIn("todo", "in_progress", "review", "done", "cancelled")]
    public string Status { get; set; } = "todo";

    [ZRequired]
    [ZEmail]
    public string AssigneeEmail { get; set; } = "";

    [ZRange(1, 100)]
    public int StoryPoints { get; set; } = 1;

    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Nested collection — each subtask validated individually
    public List<Subtask> Subtasks { get; set; } = new();

    // Nested collection — tags validated
    [ZNotEmpty]
    public List<Tag> Tags { get; set; } = new();

    public void Configure(IValidationBuilder<TaskItem> b)
    {
        b.Rule(x => x.DueDate == null || x.DueDate > x.CreatedAt,
            "Due date must be after creation date");

        b.When(x => x.Status == "done", then =>
        {
            then.Rule(x => x.Subtasks.Count == 0 || x.Subtasks.All(s => s.Status == "done"),
                "All subtasks must be done before marking task as done");
        });

        b.Rule(x => x.Subtasks.Count <= 20, "Maximum 20 subtasks per task");
    }
}

// ── Milestone (contains tasks) ──────────────────────────────────────────────

[ZValidate]
public partial class Milestone : IValidationConfigurator<Milestone>
{
    [ZRequired]
    [ZMinLength(3)]
    public string Name { get; set; } = "";

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    [ZRange(0, 100)]
    public int CompletionPercent { get; set; }

    public void Configure(IValidationBuilder<Milestone> b)
    {
        b.Property(x => x.EndDate).GreaterThan(x => x.StartDate, "End must be after start");
    }
}

// ── Project (top-level: nested objects, collections, deep nesting) ───────────

[ZValidate]
public partial class CreateProjectRequest : IValidationConfigurator<CreateProjectRequest>
{
    [ZRequired]
    [ZMinLength(3)]
    [ZMaxLength(100)]
    [ZCascade]
    public string Name { get; set; } = "";

    [ZMaxLength(1000)]
    public string? Description { get; set; }

    [ZRequired]
    [ZUrl]
    public string RepositoryUrl { get; set; } = "";

    [ZRequired]
    [ZIn("active", "planning", "on_hold", "completed")]
    public string Status { get; set; } = "planning";

    // Nested object
    public Milestone? CurrentMilestone { get; set; }

    // Nested collection of complex objects (which themselves have nested collections!)
    [ZNotEmpty]
    public List<TaskItem> Tasks { get; set; } = new();

    // Simple nested collection
    public List<Tag> Labels { get; set; } = new();

    // Team members (simple validation on collection items via parent rule)
    [ZNotEmpty]
    public List<string> TeamEmails { get; set; } = new();

    [ZRange(1, 50)]
    public int MaxTeamSize { get; set; } = 10;

    public decimal Budget { get; set; }
    public decimal SpentSoFar { get; set; }

    public void Configure(IValidationBuilder<CreateProjectRequest> b)
    {
        // Cross-field
        b.Rule(x => x.SpentSoFar <= x.Budget, "Spent cannot exceed budget");
        b.Rule(x => x.TeamEmails.Count <= x.MaxTeamSize,
            "Team size exceeds maximum");

        // Validate email format in collection (generator validates nested [ZValidate] types,
        // but strings aren't [ZValidate] — use a rule)
        b.Rule(x => x.TeamEmails.All(e => e.Contains('@')),
            "All team emails must be valid");

        // Conditional on status
        b.When(x => x.Status == "active", then =>
        {
            then.Rule(x => x.Tasks.Any(t => t.Status == "in_progress"),
                "Active project must have at least one task in progress");
            then.Rule(x => x.CurrentMilestone != null,
                "Active project must have a current milestone");
        });

        b.When(x => x.Status == "completed", then =>
        {
            then.Rule(x => x.Tasks.All(t => t.Status == "done" || t.Status == "cancelled"),
                "Completed project cannot have open tasks");
        });

        // RuleSet for different project operations
        b.RuleSet("Launch", set =>
        {
            set.Rule(x => x.Budget > 0, "Budget must be set before launch");
            set.Rule(x => x.TeamEmails.Count >= 2, "Need at least 2 team members to launch");
            set.Rule(x => x.RepositoryUrl.StartsWith("https://"),
                "Repository must use HTTPS for launch");
        });
    }
}

