---
title: Generated JSON schemas
description: Form + Table descriptors emitted as JSON, the file/embedded resource layout, and how runtime endpoints expose them to a SPA.
---

## Generated JSON

<details>
<summary><strong>Form JSON</strong></summary>

```json
{
  "name": "VoivodeshipView",
  "layout": "vertical",
  "groups": [
    { "name": "basic", "label": "Basic Info", "order": 1 },
    { "name": "contact", "label": "Contact", "order": 2 },
    { "name": "finance", "label": "Finance", "order": 3 }
  ],
  "fields": [
    {
      "name": "name", "type": "string", "uiHint": "text",
      "label": "Name", "placeholder": "Enter name...",
      "group": "basic", "order": 0, "required": true,
      "validation": { "required": true, "minLength": 2, "maxLength": 100 }
    },
    {
      "name": "code", "type": "string", "uiHint": "text",
      "label": "Code", "helpText": "Two-letter code (e.g. NY, CA)",
      "group": "basic", "order": 1, "required": true,
      "validation": { "required": true, "pattern": "^[A-Z]{2}$" }
    },
    {
      "name": "region", "type": "enum", "uiHint": "select",
      "label": "Region", "group": "basic", "order": 2,
      "options": [
        { "value": "North", "label": "North" },
        { "value": "South", "label": "South" },
        { "value": "East", "label": "East" },
        { "value": "West", "label": "West" }
      ]
    },
    {
      "name": "contactEmail", "type": "string", "uiHint": "text",
      "label": "Contact Email", "placeholder": "office@example.com",
      "group": "contact", "order": 3, "required": true,
      "validation": { "required": true, "email": true }
    },
    {
      "name": "website", "type": "string", "uiHint": "text",
      "label": "Website", "group": "contact", "order": 4, "nullable": true,
      "validation": { "url": true }
    },
    {
      "name": "establishedYear", "type": "integer", "uiHint": "number",
      "label": "Established Year", "group": "basic", "order": 5,
      "validation": { "min": 1900, "max": 2100 }
    },
    {
      "name": "hasCoastline", "type": "boolean", "uiHint": "checkbox",
      "label": "Has Coastline", "group": "basic", "order": 6,
      "conditional": { "field": "region", "operator": "equals", "value": "North" }
    },
    {
      "name": "notes", "type": "string", "uiHint": "textarea",
      "label": "Notes", "group": "finance", "order": 7,
      "props": { "rows": 3 }, "nullable": true
    },
    {
      "name": "voivodeshipId", "type": "integer", "uiHint": "number",
      "order": 8, "hidden": true
    }
  ]
}
```
</details>

<details>
<summary><strong>Table JSON</strong></summary>

```json
{
  "name": "VoivodeshipView",
  "schemaUrl": "/api/tables/voivodeship",
  "columns": [
    { "name": "id", "type": "integer", "visible": false },
    { "name": "name", "type": "string", "label": "Name",
      "sortable": true, "filterable": true },
    { "name": "code", "type": "string", "label": "Code",
      "sortable": true, "filterable": true },
    { "name": "region", "type": "enum", "label": "Region",
      "sortable": true, "filterable": true,
      "options": ["North", "South", "East", "West"] },
    { "name": "budget", "type": "decimal", "label": "Budget",
      "sortable": true, "computed": true,
      "styles": [
        { "when": "value < 0", "severity": "danger" },
        { "when": "value >= 0", "severity": "success" }
      ]
    },
    { "name": "countyCount", "type": "integer", "label": "County Count",
      "sortable": true, "computed": true },
    { "name": "establishedYear", "type": "integer", "label": "Established Year",
      "sortable": true }
  ],
  "pagination": { "defaultPageSize": 50, "pageSizes": [10, 20, 50, 100] },
  "defaultSort": { "column": "name", "direction": "asc" },
  "children": [
    { "label": "Counties", "target": "CountyView",
      "foreignKey": "voivodeshipId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/county" },
    { "label": "Postal Codes", "target": "PostalCodeView",
      "foreignKey": "voivodeshipId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/postalcode" }
  ],
  "rowActions": [
    { "name": "showDetails", "label": "Details",
      "endpoint": "/api/voivodeships/{id}", "method": "GET" },
    { "name": "generateReport", "label": "Report", "icon": "file",
      "endpoint": "/api/voivodeships/{id}/report", "method": "POST",
      "confirmation": "Generate report?" }
  ],
  "toolbarActions": [
    { "name": "export", "label": "Export to Excel", "icon": "download",
      "endpoint": "/api/voivodeships/export", "method": "GET",
      "selectionMode": "multiple" },
    { "name": "recalculate", "label": "Recalculate",
      "endpoint": "/api/voivodeships/recalculate", "method": "POST",
      "confirmation": "Recalculate balances?", "permission": "finance.write",
      "selectionMode": "none" }
  ],
  "permissions": {
    "view": "voivodeship.read",
    "columns": { "budget": "finance.read" },
    "dataFilters": ["voivodeshipId"]
  }
}
```
</details>

### API Metadata in JSON Schemas

When `[ImTiredOfCrud]` or `[CrudApi]` is present, the generated JSON schemas include API metadata:

**Table schema:**
```json
{
  "apiUrl": "/api/products",
  "keyProperty": "id",
  "columns": [
    {
      "name": "name",
      "type": "string",
      "filterable": true,
      "filterOperators": ["=", "!=", "=*", "!*", "^", "!^", "$", "!$", "=in=", "=out="]
    }
  ]
}
```

**Form schema:**
```json
{
  "apiUrl": "/api/products",
  "keyProperty": "id"
}
```

`apiUrl` — CRUD endpoint URL. `keyProperty` — ID field for GET/PATCH/DELETE by id.
`filterOperators` — per-column operators based on type (string: 10 ops, numeric: 8, enum: 4, boolean: 2).

