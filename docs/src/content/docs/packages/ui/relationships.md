---
title: Relationships ([OneToMany] / [OneToOne])
description: Modeling navigation properties so generated forms render relation pickers and tables understand drill-downs.
---

## Relationships (`[OneToMany]` / `[OneToOne]`)

Define relationships on navigation properties — a single declaration drives both table drill-down and form sub-forms:

```csharp
[UiTable(SchemaUrl = "/api/tables/task")]
[UiForm]
public partial class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ProjectId { get; set; }  // FK auto-detected by convention
}

[UiForm]
public partial class ProjectSettings
{
    public int Id { get; set; }
    public string Theme { get; set; } = "";
}

[UiForm]
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/project")]
public partial class ProjectView
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiFormField(Label = "Project Name")]
    [UiTableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    public int SettingsId { get; set; }

    // One-to-many: FK auto-detected as TaskItem.ProjectId
    [OneToMany(Label = "Tasks")]
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    // Explicit FK via nameof() for compile-time safety
    [OneToMany(ForeignKey = nameof(Attachment.ProjectId), Label = "Attachments")]
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    // One-to-one: FK auto-detected as ProjectView.SettingsId
    [OneToOne(Label = "Settings")]
    public ProjectSettings? Settings { get; set; }
}
```

Navigation properties are automatically excluded from form fields and table columns.

### Foreign Key Resolution

1. **Explicit** — `[OneToMany(ForeignKey = nameof(Child.ParentId))]` (compile-time safe)
2. **Convention (OneToMany)** — looks for `{ParentTypeName}Id` on the child type (strips `View` suffix)
3. **Convention (OneToOne)** — looks for `{NavigationPropertyName}Id` on the parent type

### SchemaUrl / FormSchemaUrl Resolution

Both `[OneToMany]` and `[OneToOne]` resolve URLs with the same priority:
1. Explicit property on the attribute
2. From target type's `[UiTable]` / `[UiForm]` attribute
3. Convention fallback (e.g. `/api/tables/{name}`, `/api/forms/{name}`)

### Generated JSON

Table JSON includes a `relation` field:
```json
{
  "children": [
    {
      "label": "Tasks", "target": "TaskItem",
      "foreignKey": "projectId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/task", "formSchemaUrl": "/api/forms/taskitem"
    },
    {
      "label": "Settings", "target": "ProjectSettings",
      "foreignKey": "settingsId", "relation": "oneToOne",
      "formSchemaUrl": "/api/forms/projectsettings"
    }
  ]
}
```

Form JSON also includes a `children` block:
```json
{
  "children": [
    {
      "name": "tasks", "label": "Tasks", "target": "TaskItem",
      "foreignKey": "projectId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/task", "formSchemaUrl": "/api/forms/taskitem"
    }
  ]
}
```

### Backward Compatibility


