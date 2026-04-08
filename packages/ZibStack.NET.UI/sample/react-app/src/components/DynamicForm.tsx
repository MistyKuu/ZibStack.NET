import { useForm } from 'react-hook-form';
import { useFormSchema, FieldSchema } from '../hooks/useFormSchema';
import { DynamicField } from './DynamicField';

interface Props {
  /** URL to the form schema endpoint, e.g. "/api/forms/player" */
  schemaUrl: string;
  /** 'create' hides updateOnly fields, 'edit' hides createOnly fields */
  mode?: 'create' | 'edit';
  /** Pre-fill form values (for edit mode) */
  defaultValues?: Record<string, any>;
  /** Called with form data on valid submit */
  onSubmit: (data: Record<string, any>) => void | Promise<void>;
}

/**
 * A fully dynamic form rendered from a ZibStack.NET.UI JSON schema.
 *
 * Usage:
 *   <DynamicForm schemaUrl="/api/forms/player" mode="create" onSubmit={handleSave} />
 */
export function DynamicForm({ schemaUrl, mode = 'create', defaultValues, onSubmit }: Props) {
  const { schema, loading, error } = useFormSchema(schemaUrl);
  const { control, handleSubmit, watch } = useForm({ defaultValues: defaultValues ?? {} });
  const values = watch(); // live values for conditional visibility

  if (loading) return <div>Loading form...</div>;
  if (error) return <div>Error loading form: {error}</div>;
  if (!schema) return null;

  const groups = [...schema.groups].sort((a, b) => a.order - b.order);

  const filterField = (f: FieldSchema) => {
    if (mode === 'create' && f.updateOnly) return false;
    if (mode === 'edit' && f.createOnly) return false;
    return true;
  };

  const fieldsForGroup = (groupName: string) =>
    schema.fields
      .filter(f => f.group === groupName)
      .filter(filterField)
      .sort((a, b) => a.order - b.order);

  const ungroupedFields = schema.fields
    .filter(f => !f.group)
    .filter(filterField)
    .sort((a, b) => a.order - b.order);

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      <h3>{schema.name}</h3>

      {groups.map(group => (
        <fieldset key={group.name} style={{ marginBottom: '1.5rem', padding: '1rem', border: '1px solid #ddd', borderRadius: '8px' }}>
          <legend style={{ fontWeight: 'bold' }}>{group.label ?? group.name}</legend>
          {fieldsForGroup(group.name).map(field => (
            <DynamicField key={field.name} field={field} control={control} values={values} />
          ))}
        </fieldset>
      ))}

      {ungroupedFields.map(field => (
        <DynamicField key={field.name} field={field} control={control} values={values} />
      ))}

      <button type="submit" style={{
        background: '#0078d4', color: 'white', border: 'none',
        padding: '0.75rem 2rem', borderRadius: '4px', cursor: 'pointer', fontSize: '1rem'
      }}>
        Save
      </button>
    </form>
  );
}
