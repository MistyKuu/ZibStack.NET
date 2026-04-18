using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

// ── Nested validation demo ────────────────────────────────────────────────────
// Address is validated on its own, ContactForm auto-validates both addresses.
// ShippingAddress is nullable — skipped when null.

[ZValidate]
public partial class Address
{
    [ZRequired]
    public string Street { get; set; } = "";

    [ZRequired]
    public string City { get; set; } = "";

    [ZMatch(@"^\d{2}-\d{3}$", Message = "Zip must be XX-XXX format")]
    public string? Zip { get; set; }
}

[ZValidate]
public partial class ContactForm : IValidationConfigurator<ContactForm>
{
    [ZRequired]
    [ZEmail]
    public string Email { get; set; } = "";

    [ZRequired]
    public string Phone { get; set; } = "";

    // Nested — auto-validated with path prefix ("BillingAddress.Street is required.")
    public Address BillingAddress { get; set; } = new();

    // Nullable nested — skipped when null, validated when present
    public Address? ShippingAddress { get; set; }

    // Items in collection — each validated with index ("Notes[0].Text is required.")
    public List<Note> Notes { get; set; } = new();

    public void Configure(IValidationBuilder<ContactForm> b)
    {
        // Cross-field: phone or email must be provided (at least one)
        b.Rule(x => !string.IsNullOrEmpty(x.Phone) || !string.IsNullOrEmpty(x.Email),
            "At least one of Phone or Email is required");

        // Collection count rule
        b.Rule(x => x.Notes.Count <= 5, "Maximum 5 notes allowed");
    }
}

[ZValidate]
public partial class Note
{
    [ZRequired]
    [ZMaxLength(500)]
    public string Text { get; set; } = "";
}
