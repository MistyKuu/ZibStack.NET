import { useState } from 'react';
import { useTableSchema, ColumnSchema } from '../hooks/useFormSchema';

interface Props {
  /** URL to the table schema endpoint, e.g. "/api/tables/player" */
  schemaUrl: string;
  /** The data rows to display */
  data: Record<string, any>[];
  /** Total number of items (for pagination) */
  totalCount: number;
  /** Called when sort/filter/page changes */
  onQueryChange?: (query: TableQuery) => void;
}

export interface TableQuery {
  page: number;
  pageSize: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
  filters: Record<string, string>;
}

/**
 * A dynamic data table rendered from a ZibStack.NET.UI JSON table schema.
 *
 * Usage:
 *   <DynamicTable schemaUrl="/api/tables/player" data={players} totalCount={100} onQueryChange={handleQuery} />
 */
export function DynamicTable({ schemaUrl, data, totalCount, onQueryChange }: Props) {
  const { schema, loading, error } = useTableSchema(schemaUrl);
  const [query, setQuery] = useState<TableQuery>({
    page: 1,
    pageSize: 20,
    sortBy: undefined,
    sortDirection: undefined,
    filters: {},
  });

  if (loading) return <div>Loading table...</div>;
  if (error) return <div>Error loading table: {error}</div>;
  if (!schema) return null;

  // Initialize from schema defaults
  if (query.pageSize === 20 && schema.pagination.defaultPageSize !== 20) {
    setQuery(q => ({ ...q, pageSize: schema.pagination.defaultPageSize }));
  }

  const visibleColumns = schema.columns
    .filter(c => c.visible !== false)
    .sort((a, b) => a.order - b.order);

  const totalPages = Math.ceil(totalCount / query.pageSize);

  const updateQuery = (updates: Partial<TableQuery>) => {
    const newQuery = { ...query, ...updates };
    setQuery(newQuery);
    onQueryChange?.(newQuery);
  };

  const toggleSort = (col: ColumnSchema) => {
    if (!col.sortable) return;
    const newDir = query.sortBy === col.name && query.sortDirection === 'asc' ? 'desc' : 'asc';
    updateQuery({ sortBy: col.name, sortDirection: newDir, page: 1 });
  };

  const updateFilter = (colName: string, value: string) => {
    const filters = { ...query.filters, [colName]: value };
    if (!value) delete filters[colName];
    updateQuery({ filters, page: 1 });
  };

  return (
    <div>
      <h3>{schema.name}</h3>

      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          {/* Column headers with sort indicators */}
          <tr>
            {visibleColumns.map(col => (
              <th key={col.name}
                  onClick={() => toggleSort(col)}
                  style={{
                    padding: '0.75rem', textAlign: 'left', borderBottom: '2px solid #ddd',
                    cursor: col.sortable ? 'pointer' : 'default',
                    userSelect: 'none', width: col.width ?? 'auto',
                  }}>
                {col.label ?? col.name}
                {col.sortable && query.sortBy === col.name && (
                  <span> {query.sortDirection === 'asc' ? '▲' : '▼'}</span>
                )}
              </th>
            ))}
          </tr>

          {/* Filter row */}
          <tr>
            {visibleColumns.map(col => (
              <th key={`filter-${col.name}`} style={{ padding: '0.25rem' }}>
                {col.filterable && (
                  col.options ? (
                    <select
                      value={query.filters[col.name] ?? ''}
                      onChange={e => updateFilter(col.name, e.target.value)}
                      style={{ width: '100%', padding: '0.25rem' }}>
                      <option value="">All</option>
                      {col.options.map(o => <option key={o} value={o}>{o}</option>)}
                    </select>
                  ) : (
                    <input
                      type="text"
                      placeholder={`Filter ${col.label ?? col.name}...`}
                      value={query.filters[col.name] ?? ''}
                      onChange={e => updateFilter(col.name, e.target.value)}
                      style={{ width: '100%', padding: '0.25rem', boxSizing: 'border-box' }} />
                  )
                )}
              </th>
            ))}
          </tr>
        </thead>

        <tbody>
          {data.map((row, i) => (
            <tr key={i} style={{ borderBottom: '1px solid #eee' }}>
              {visibleColumns.map(col => (
                <td key={col.name} style={{ padding: '0.75rem' }}>
                  {formatCell(row[col.name], col)}
                </td>
              ))}
            </tr>
          ))}
          {data.length === 0 && (
            <tr>
              <td colSpan={visibleColumns.length} style={{ padding: '2rem', textAlign: 'center', color: '#999' }}>
                No data
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {/* Pagination */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '1rem' }}>
        <div>
          <span>Page size: </span>
          <select value={query.pageSize} onChange={e => updateQuery({ pageSize: +e.target.value, page: 1 })}>
            {schema.pagination.pageSizes.map(s => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>

        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          <button disabled={query.page <= 1} onClick={() => updateQuery({ page: query.page - 1 })}>
            Previous
          </button>
          <span>Page {query.page} of {totalPages || 1}</span>
          <button disabled={query.page >= totalPages} onClick={() => updateQuery({ page: query.page + 1 })}>
            Next
          </button>
        </div>

        <div>
          Total: {totalCount} items
        </div>
      </div>
    </div>
  );
}

function formatCell(value: any, col: ColumnSchema): string {
  if (value == null) return '';
  if (col.format && col.type === 'date') {
    try { return new Date(value).toLocaleDateString(); } catch { /* fall through */ }
  }
  return String(value);
}
