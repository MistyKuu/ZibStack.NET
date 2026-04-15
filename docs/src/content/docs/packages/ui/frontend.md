---
title: Frontend integration
description: How a SPA consumes the generated JSON form/table descriptors — schemaUrl, runtime endpoints, dynamic field rendering.
---

## Frontend Integration

### Razor Pages (server-side)

Use the `FormDescriptor` directly in `.cshtml` — no JSON, no JavaScript:

```cshtml
@{
    var form = CreateVoivodeshipRequest.GetFormDescriptor();
}
<h3>@form.Name</h3>
<form method="post">
    @foreach (var group in form.Groups.OrderBy(g => g.Order))
    {
        <fieldset>
            <legend>@group.Label</legend>
            @foreach (var field in form.Fields.Where(f => f.Group == group.Name).OrderBy(f => f.Order))
            {
                <div class="form-group">
                    <label>@field.Label</label>
                    @switch (field.UiHint)
                    {
                        case "text":     <input type="text" name="@field.Name" placeholder="@field.Placeholder" /> break;
                        case "number":   <input type="number" name="@field.Name" /> break;
                        case "select":   <select name="@field.Name">@foreach (var o in field.Options!) { <option value="@o.Value">@o.Label</option> }</select> break;
                        case "textarea": <textarea name="@field.Name" rows="@field.Props!["rows"]"></textarea> break;
                        case "checkbox": <input type="checkbox" name="@field.Name" /> break;
                    }
                </div>
            }
        </fieldset>
    }
    <button type="submit">Save</button>
</form>
```

### Blazor

```razor
@inject HttpClient Http

@if (_schema is not null)
{
    @foreach (var field in _schema.Fields.OrderBy(f => f.Order))
    {
        <div class="form-group">
            <label>@field.Label</label>
            @switch (field.UiHint)
            {
                case "text":     <input type="text" placeholder="@field.Placeholder"
                                        @oninput="e => _values[field.Name] = e.Value" /> break;
                case "select":   <select @onchange="e => _values[field.Name] = e.Value">
                                     @foreach (var o in field.Options ?? []) { <option value="@o.Value">@o.Label</option> }
                                 </select> break;
                case "slider":   <input type="range" min="@field.Props["min"]" max="@field.Props["max"]"
                                        @oninput="e => _values[field.Name] = e.Value" /> break;
                case "textarea": <textarea rows="@field.Props["rows"]"
                                           @oninput="e => _values[field.Name] = e.Value" /> break;
            }
        </div>
    }
}

@code {
    private FormSchema? _schema;
    private Dictionary<string, object?> _values = new();

    protected override async Task OnInitializedAsync()
        => _schema = await Http.GetFromJsonAsync<FormSchema>("/api/forms/voivodeship");
}
```

See [SampleBlazor](sample/SampleBlazor/) for full DynamicField/DynamicForm/ErpDemo with drill-down, row actions, and conditional styling.

### React

```tsx
import { useForm, Controller } from 'react-hook-form';

function DynamicForm({ schemaUrl, onSubmit }) {
  const [schema, setSchema] = useState(null);
  const { control, handleSubmit, watch } = useForm();
  const values = watch();

  useEffect(() => { fetch(schemaUrl).then(r => r.json()).then(setSchema); }, [schemaUrl]);
  if (!schema) return <div>Loading...</div>;

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      {schema.fields
        .filter(f => !f.conditional || values[f.conditional.field] === f.conditional.value)
        .sort((a, b) => a.order - b.order)
        .map(field => (
          <Controller key={field.name} name={field.name} control={control}
            rules={{ required: field.required && `${field.label} is required` }}
            render={({ field: f, fieldState: { error } }) => (
              <div>
                <label>{field.label}</label>
                {field.uiHint === 'select'
                  ? <select {...f}>{field.options?.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}</select>
                  : field.uiHint === 'textarea'
                  ? <textarea {...f} rows={field.props?.rows} />
                  : <input type={field.uiHint === 'password' ? 'password' : 'text'} {...f} placeholder={field.placeholder} />}
                {error && <span>{error.message}</span>}
              </div>
            )} />
        ))}
      <button type="submit">Save</button>
    </form>
  );
}
```

See [react-app](sample/react-app/) for full DynamicField, DynamicForm, DynamicTable, and ErpTable components.

