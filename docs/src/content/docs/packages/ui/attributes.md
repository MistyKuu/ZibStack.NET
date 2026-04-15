---
title: All attributes
description: Full reference for every UI attribute (forms, tables, fields, columns, groups) — exact parameters and defaults.
---

## All Attributes

### Form — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[UiForm]` | Mark for form generation | `Name?`, `Layout?` |
| `[UiFormGroup("name")]` | Define field group | `Label?`, `Order?` (AllowMultiple) |

### Form — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[UiFormField]` | Customize field | `Label?`, `Placeholder?`, `HelpText?`, `Order?`, `Group?` |
| `[UiFormIgnore]` | Exclude from form | — |
| `[UiFormHidden]` | In data but not rendered | — |
| `[UiFormOrder(n)]` | Explicit ordering | `int order` |
| `[UiFormReadOnly]` | Read-only field | — |
| `[UiFormDisabled]` | Disabled field | — |
| `[UiFormSection("group")]` | Assign to group | `string group` |
| `[UiFormConditional("field", "value")]` | Conditional visibility | `Operator?` |

### UI Control Hints — Property-level

| Attribute | Control | Extra |
|-----------|---------|-------|
| `[TextArea]` | Multi-line text | `Rows?` |
| `[Select]` | Dropdown | `Type enumType` or `params string[]` |
| `[RadioGroup]` | Radio buttons | `Type enumType` or `params string[]` |
| `[Checkbox]` | Toggle | — |
| `[DatePicker]` | Date selector | `Min?`, `Max?` |
| `[TimePicker]` | Time selector | — |
| `[DateTimePicker]` | Date + time | — |
| `[FilePicker]` | File upload | `Accept?`, `Multiple?` |
| `[ColorPicker]` | Color selector | — |
| `[RichText]` | Rich text editor | — |
| `[Slider]` | Range slider | `Min`, `Max`, `Step?` |
| `[PasswordInput]` | Masked input | — |

### Table — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[UiTable]` | Mark for table generation | `Name?`, `DefaultPageSize?`, `PageSizes?`, `DefaultSort?`, `DefaultSortDirection?`, `SchemaUrl?` |

### Table — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[UiTableColumn]` | Customize column | `Label?`, `Sortable?`, `Filterable?`, `Format?`, `Order?`, `IsVisible?`, `Width?` |
| `[UiTableIgnore]` | Exclude from table | — |

### Relationships — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[OneToMany]` | One-to-many on `ICollection<T>` | `ForeignKey?`, `Label?`, `SchemaUrl?`, `FormSchemaUrl?` |
| `[OneToOne]` | One-to-one on navigation property | `ForeignKey?`, `Label?`, `SchemaUrl?`, `FormSchemaUrl?` |

### Entity — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[Entity]` | Generate `IEntityTypeConfiguration<T>` for EF Core | `TableName?`, `Schema?` |

### ERP — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[RowAction("name")]` | Per-row action button | `Label`, `Icon?`, `Endpoint`, `Method?`, `Confirmation?`, `Permission?` (AllowMultiple) |
| `[ToolbarAction("name")]` | Global toolbar action | `Label`, `Icon?`, `Endpoint`, `Method?`, `Confirmation?`, `Permission?`, `SelectionMode?` (AllowMultiple) |
| `[Permission("name")]` | Required view permission | — |
| `[ColumnPermission("col", "perm")]` | Per-column permission | (AllowMultiple) |
| `[DataFilter("prop")]` | Vertical data filtering | (AllowMultiple) |

### ERP — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[Computed]` | Marks virtual/calculated column | — |
| `[ColumnStyle]` | Conditional styling | `When`, `Severity` (danger/warning/success/info/muted) (AllowMultiple) |

## SchemaUrl Resolution


1. **Explicit** — `SchemaUrl = "/custom/url"` on the attribute itself
2. **From target type** — `[UiTable(SchemaUrl = "/api/tables/county")]` on `T` itself
3. **Convention** — strip `View` suffix, lowercase → `/api/tables/{name}` (e.g. `CountyView` → `/api/tables/county`)

`FormSchemaUrl` (available on `[OneToMany]`/`[OneToOne]`) follows the same pattern but checks for `[UiForm]` on the target type.

This means you typically declare `SchemaUrl` once on the child type's `[UiTable]` and it propagates to all parents that reference it.

## Default Behavior

- All public properties included unless `[UiFormIgnore]` / `[UiTableIgnore]`
- UI hint auto-detected from C# type: `string` → text, `bool` → checkbox, `enum` → select, `DateTime` → datePicker
- Labels humanized from property names: `FirstName` → "First Name"
- A class can have both `[UiForm]` and `[UiTable]`

## Cross-Package Integration

### ZibStack.NET.Validation

When referenced, validation attributes are automatically included in form field metadata:

| Attribute | JSON output |
|-----------|-------------|
| `[ZRequired]` | `"validation": { "required": true }` |
| `[ZMinLength(n)]` | `"validation": { "minLength": n }` |
| `[ZMaxLength(n)]` | `"validation": { "maxLength": n }` |
| `[ZRange(min, max)]` | `"validation": { "min": min, "max": max }` |
| `[ZEmail]` | `"validation": { "email": true }` |
| `[ZUrl]` | `"validation": { "url": true }` |
| `[ZMatch("regex")]` | `"validation": { "pattern": "regex" }` |
| `[ZNotEmpty]` | `"validation": { "notEmpty": true }` |

Also recognizes `System.ComponentModel.DataAnnotations` equivalents (`[ZRequired]`, `[ZMinLength]`, `[ZMaxLength]`, `[ZRange]`, `[StringLength]`).

### ZibStack.NET.Dto

When referenced, `[DtoOnly(DtoTarget.Create)]` and `[DtoOnly(DtoTarget.Update)]` flags appear in form field descriptors — the client can show/hide fields based on create vs. update mode.

No project-level dependencies — detection is by attribute FQN at compile time.
