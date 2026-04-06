import { Control, Controller } from 'react-hook-form';
import { FieldSchema } from '../hooks/useFormSchema';

interface Props {
  field: FieldSchema;
  control: Control<any>;
  values: Record<string, any>;
}

/**
 * Renders a single form field based on the ZibStack.NET.UI schema.
 * Handles all UI hints, conditional visibility, and validation rules.
 */
export function DynamicField({ field, control, values }: Props) {
  // ── Conditional visibility ───────────────────────────────────────────
  if (field.conditional) {
    const depValue = String(values[field.conditional.field] ?? '');
    const expected = field.conditional.value;

    switch (field.conditional.operator) {
      case 'equals':    if (depValue !== expected) return null; break;
      case 'notEquals': if (depValue === expected) return null; break;
      case 'contains':  if (!depValue.includes(expected)) return null; break;
      case 'greaterThan': if (!(Number(depValue) > Number(expected))) return null; break;
      case 'lessThan':    if (!(Number(depValue) < Number(expected))) return null; break;
    }
  }

  if (field.hidden) return null;

  // ── Validation rules from schema → react-hook-form ───────────────────
  const rules: Record<string, any> = {};
  if (field.required)
    rules.required = `${field.label ?? field.name} is required`;
  if (field.validation?.minLength)
    rules.minLength = { value: Number(field.validation.minLength), message: `Min ${field.validation.minLength} characters` };
  if (field.validation?.maxLength)
    rules.maxLength = { value: Number(field.validation.maxLength), message: `Max ${field.validation.maxLength} characters` };
  if (field.validation?.min)
    rules.min = { value: Number(field.validation.min), message: `Minimum: ${field.validation.min}` };
  if (field.validation?.max)
    rules.max = { value: Number(field.validation.max), message: `Maximum: ${field.validation.max}` };
  if (field.validation?.pattern)
    rules.pattern = { value: new RegExp(field.validation.pattern), message: 'Invalid format' };
  if (field.validation?.email)
    rules.pattern = { value: /^[^@\s]+@[^@\s]+\.[^@\s]+$/, message: 'Invalid email address' };
  if (field.validation?.url)
    rules.pattern = { value: /^https?:\/\/.+/, message: 'Invalid URL' };

  return (
    <div className="form-field" style={{ marginBottom: '1rem' }}>
      <label htmlFor={field.name} style={{ display: 'block', fontWeight: 600, marginBottom: '0.25rem' }}>
        {field.label ?? field.name}
        {field.required && <span style={{ color: 'red' }}> *</span>}
      </label>

      <Controller
        name={field.name}
        control={control}
        rules={rules}
        render={({ field: f, fieldState: { error } }) => (
          <>
            {renderInput(field, f)}
            {field.helpText && (
              <small style={{ display: 'block', color: '#666', marginTop: '0.25rem' }}>
                {field.helpText}
              </small>
            )}
            {error && (
              <span style={{ display: 'block', color: 'red', fontSize: '0.85rem', marginTop: '0.25rem' }}>
                {error.message}
              </span>
            )}
          </>
        )}
      />
    </div>
  );
}

function renderInput(schema: FieldSchema, field: any) {
  const props = schema.props ?? {};
  const baseStyle = { width: '100%', padding: '0.5rem', border: '1px solid #ccc', borderRadius: '4px', boxSizing: 'border-box' as const };

  switch (schema.uiHint) {
    case 'text':
      return (
        <input {...field} type="text" style={baseStyle}
               placeholder={schema.placeholder}
               disabled={schema.disabled} readOnly={schema.readOnly} />
      );

    case 'password':
      return (
        <input {...field} type="password" style={baseStyle}
               placeholder={schema.placeholder} />
      );

    case 'number':
      return (
        <input {...field} type="number" style={baseStyle}
               onChange={e => field.onChange(e.target.value === '' ? null : +e.target.value)} />
      );

    case 'textarea':
      return (
        <textarea {...field} rows={props.rows ?? 3} style={{ ...baseStyle, resize: 'vertical' }}
                  placeholder={schema.placeholder} />
      );

    case 'checkbox':
      return (
        <input type="checkbox" checked={!!field.value}
               onChange={e => field.onChange(e.target.checked)} />
      );

    case 'select':
      return (
        <select {...field} style={baseStyle}>
          <option value="">-- Select --</option>
          {schema.options?.map(o => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
      );

    case 'radioGroup':
      return (
        <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
          {schema.options?.map(o => (
            <label key={o.value} style={{ display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
              <input type="radio" value={o.value}
                     checked={field.value === o.value}
                     onChange={() => field.onChange(o.value)} />
              {o.label}
            </label>
          ))}
        </div>
      );

    case 'slider':
      return (
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <input type="range" {...field} style={{ flex: 1 }}
                 min={props.min ?? 0} max={props.max ?? 100} step={props.step ?? 1}
                 onChange={e => field.onChange(+e.target.value)} />
          <span style={{ minWidth: '2rem', textAlign: 'right' }}>{field.value ?? 0}</span>
        </div>
      );

    case 'datePicker':
      return <input {...field} type="date" style={baseStyle} />;

    case 'dateTimePicker':
      return <input {...field} type="datetime-local" style={baseStyle} />;

    case 'timePicker':
      return <input {...field} type="time" style={baseStyle} />;

    case 'filePicker':
      return (
        <input type="file" accept={props.accept as string}
               multiple={!!props.multiple}
               onChange={e => field.onChange(e.target.files)} />
      );

    case 'colorPicker':
      return <input {...field} type="color" />;

    case 'richText':
      return (
        <textarea {...field} rows={6} style={{ ...baseStyle, resize: 'vertical' }} />
      );

    default:
      return <input {...field} type="text" style={baseStyle} />;
  }
}
