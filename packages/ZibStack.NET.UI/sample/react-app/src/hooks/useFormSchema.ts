import { useEffect, useState } from 'react';

export interface FormSchema {
  name: string;
  layout: string;
  groups: GroupSchema[];
  fields: FieldSchema[];
}

export interface GroupSchema {
  name: string;
  label?: string;
  order: number;
}

export interface FieldSchema {
  name: string;
  type: string;
  uiHint: string;
  label?: string;
  placeholder?: string;
  helpText?: string;
  group?: string;
  order: number;
  required?: boolean;
  hidden?: boolean;
  readOnly?: boolean;
  disabled?: boolean;
  createOnly?: boolean;
  updateOnly?: boolean;
  nullable?: boolean;
  options?: { value: string; label: string }[];
  conditional?: { field: string; operator: string; value: string };
  validation?: Record<string, any>;
  props?: Record<string, any>;
}

export interface TableSchema {
  name: string;
  columns: ColumnSchema[];
  pagination: { defaultPageSize: number; pageSizes: number[] };
  defaultSort?: { column: string; direction: string };
}

export interface ColumnSchema {
  name: string;
  type: string;
  label?: string;
  sortable: boolean;
  filterable: boolean;
  format?: string;
  order: number;
  visible?: boolean;
  width?: string;
  options?: string[];
}

/**
 * Fetches a form schema from the given API URL.
 * In production, this would hit your .NET API endpoint that serves
 * Player.GetFormSchemaJson() or similar.
 */
export function useFormSchema(url: string) {
  const [schema, setSchema] = useState<FormSchema | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    fetch(url)
      .then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return r.json();
      })
      .then(data => { setSchema(data); setLoading(false); })
      .catch(err => { setError(err.message); setLoading(false); });
  }, [url]);

  return { schema, loading, error };
}

/**
 * Fetches a table schema from the given API URL.
 */
export function useTableSchema(url: string) {
  const [schema, setSchema] = useState<TableSchema | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    fetch(url)
      .then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return r.json();
      })
      .then(data => { setSchema(data); setLoading(false); })
      .catch(err => { setError(err.message); setLoading(false); });
  }, [url]);

  return { schema, loading, error };
}
