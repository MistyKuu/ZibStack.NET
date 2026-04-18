using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

// ── Nested validation demo ────────────────────────────────────────────────────
// All validation defined via fluent IValidationConfigurator — no attributes needed.
// Address validates itself, ContactForm auto-validates nested addresses + notes.

[ZValidate]
public partial class Address : IValidationConfigurator<Address>
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string? Zip { get; set; }

    public void Configure(IValidationBuilder<Address> b)
    {
        b.Property(x => x.Street).Required();
        b.Property(x => x.City).Required();
        b.Property(x => x.Zip).Match(@"^\d{2}-\d{3}$", "Zip must be XX-XXX format");
    }
}

[ZValidate]
public partial class ContactForm : IValidationConfigurator<ContactForm>
{
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";

    // Nested — auto-validated with path prefix ("BillingAddress.Street is required.")
    public Address BillingAddress { get; set; } = new();

    // Nullable nested — skipped when null, validated when present
    public Address? ShippingAddress { get; set; }

    // Items in collection — each validated with index ("Notes[0].Text is required.")
    public List<Note> Notes { get; set; } = new();

    public void Configure(IValidationBuilder<ContactForm> b)
    {
        // Per-property fluent rules (equivalent to [ZRequired], [ZEmail])
        b.Property(x => x.Email).Required().Email();
        b.Property(x => x.Phone).Required();

        // Cross-field: at least one contact method
        b.Rule(x => !string.IsNullOrEmpty(x.Phone) || !string.IsNullOrEmpty(x.Email),
            "At least one of Phone or Email is required");

        // Collection count rule
        b.Rule(x => x.Notes.Count <= 5, "Maximum 5 notes allowed");
    }
}

[ZValidate]
public partial class Note : IValidationConfigurator<Note>
{
    public string Text { get; set; } = "";

    public void Configure(IValidationBuilder<Note> b)
    {
        b.Property(x => x.Text).Required().MaxLength(500);
    }
}
