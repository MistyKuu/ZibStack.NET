---
title: Validation Attributes
description: Complete reference for all validation attributes — [ZRequired], [ZRange], [ZEmail], [ZIn], [ZCreditCard], [ZCascade] and more.
---

## Attribute Reference

| Attribute | Applies to | Description |
|-----------|-----------|-------------|
| `[ZValidate]` | class / record / struct | Marks the type for code generation (must be `partial`) |
| `[ZRequired]` | property | Not null; for strings also not empty/whitespace |
| `[ZMinLength(n)]` | property (string / collection) | Minimum length or item count |
| `[ZMaxLength(n)]` | property (string / collection) | Maximum length or item count |
| `[ZRange(min, max)]` | property (numeric) | Inclusive numeric range |
| `[ZEmail]` | property (string) | Must match email regex |
| `[ZUrl]` | property (string) | Must be a valid absolute URI |
| `[ZMatch(pattern)]` | property (string) | Must match the given regex pattern |
| `[ZNotEmpty]` | property (collection / string) | Collection must have items; string must not be whitespace |
| `[ZIn("a","b","c")]` | property | Value must be one of the allowed values |
| `[ZNotIn("x","y")]` | property | Value must NOT be any of the specified values |
| `[ZCreditCard]` | property (string) | Must pass the Luhn algorithm check |
| `[ZPhone]` | property (string) | Must match phone number format |
| `[ZCascade]` | property | Stop after first rule failure for this property |

## Custom Error Messages

All attributes accept a `Message` property for custom error text:

```csharp
[ZRequired(Message = "Please provide your name")]
[ZRange(1, 100, Message = "{PropertyName} must be 1-100, got {PropertyValue}")]
[ZMatch(@"^\d{3}-\d{4}$", Message = "Phone must be in format XXX-XXXX")]
```

### Message Placeholders

| Placeholder | Replaced with |
|-------------|---------------|
| `{PropertyName}` | The property name (e.g. "Email") |
| `{PropertyValue}` | The actual value at runtime |

## CascadeMode — `[ZCascade]`

By default, all rules for a property are evaluated and all errors are reported. Add `[ZCascade]` to stop after the first failure:

```csharp
[ZValidate]
public partial class LoginRequest
{
    [ZRequired] [ZMinLength(3)] [ZMaxLength(50)] [ZCascade]
    public string Username { get; set; } = "";
}
```

If `Username` is empty → only `"Username is required."` is reported. `MinLength` and `MaxLength` are skipped.

Without `[ZCascade]` → all three errors would be reported.

## `[ZIn]` and `[ZNotIn]`

```csharp
[ZValidate]
public partial class StatusUpdate
{
    [ZIn("draft", "active", "archived")]
    public string Status { get; set; } = "";

    [ZNotIn("admin", "root", "system")]
    public string Username { get; set; } = "";
}
```

## `[ZCreditCard]` — Luhn Algorithm

```csharp
[ZValidate]
public partial class PaymentForm
{
    [ZRequired] [ZCreditCard]
    public string CardNumber { get; set; } = "";
}

// Valid: "4111111111111111" (Visa test number)
// Invalid: "1234567890123456"
```

## `[ZPhone]`

```csharp
[ZPhone]
public string? PhoneNumber { get; set; }

// Valid: "+1-555-123-4567", "(555) 123 4567", "+48501234567"
// Invalid: "abc", "12"
```
