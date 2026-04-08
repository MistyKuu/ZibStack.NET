import { useState } from 'react';
import { useTableSchema, ColumnSchema } from '../hooks/useFormSchema';

interface Props {
  schemaUrl: string;
  data: Record<string, any>[];
  totalCount: number;
  onDrillDown?: (target: string, foreignKey: string, parentId: any) => void;
}

/**
 * ERP-style table with drill-down, row actions, toolbar actions,
 * computed columns, conditional styling, and permission metadata.
 */
export function ErpTable({ schemaUrl, data, totalCount, onDrillDown }: Props) {
  const { schema, loading, error } = useTableSchema(schemaUrl);
  const [actionLog, setActionLog] = useState<string[]>([]);
  const [selectedRows, setSelectedRows] = useState<Set<number>>(new Set());

  if (loading) return <div>Loading...</div>;
  if (error) return <div>Error: {error}</div>;
  if (!schema) return null;

  const raw = schema as any;
  const children = raw.children ?? [];
  const rowActions = raw.rowActions ?? [];
  const toolbarActions = raw.toolbarActions ?? [];
  const permissions = raw.permissions;

  const visibleColumns = (schema.columns as any[])
    .filter((c: any) => c.visible !== false)
    .sort((a: any, b: any) => a.order - b.order);

  const log = (msg: string) => setActionLog(prev => [...prev, msg]);

  const handleRowAction = (action: any, row: Record<string, any>) => {
    const endpoint = action.endpoint.replace('{id}', row.id);
    if (action.confirmation && !confirm(action.confirmation)) return;
    log(`${action.method} ${endpoint} — ${action.label}`);
  };

  const handleToolbarAction = (action: any) => {
    if (action.confirmation && !confirm(action.confirmation)) return;
    const ids = action.selectionMode === 'multiple'
      ? Array.from(selectedRows).join(',')
      : '';
    log(`Toolbar: ${action.label} → ${action.endpoint}${ids ? `?ids=${ids}` : ''}`);
  };

  const handleDrillDown = (child: any, row: Record<string, any>) => {
    log(`Drill-down: ${child.label} → ${child.target}?${child.foreignKey}=${row.id}`);
    onDrillDown?.(child.target, child.foreignKey, row.id);
  };

  const getCellStyle = (col: any, value: any): React.CSSProperties => {
    if (!col.styles) return {};
    const num = Number(value);
    if (isNaN(num)) return {};

    for (const style of col.styles) {
      let matches = false;
      if (style.when === 'value < 0') matches = num < 0;
      else if (style.when === 'value >= 0') matches = num >= 0;
      else if (style.when === 'value > 1000') matches = num > 1000;

      if (matches) {
        const severityMap: Record<string, React.CSSProperties> = {
          danger: { color: 'red', fontWeight: 'bold' },
          warning: { color: 'orange' },
          success: { color: 'green', fontWeight: 'bold' },
          info: { color: 'blue' },
          muted: { color: '#999' },
        };
        return severityMap[style.severity] ?? {};
      }
    }
    return {};
  };

  const toggleRow = (i: number) => {
    setSelectedRows(prev => {
      const next = new Set(prev);
      next.has(i) ? next.delete(i) : next.add(i);
      return next;
    });
  };

  const hasMultiSelectAction = toolbarActions.some((a: any) => a.selectionMode === 'multiple');

  return (
    <div>
      <h3>{schema.name}</h3>

      {/* Permission info */}
      {permissions && (
        <details style={{ marginBottom: '0.5rem', fontSize: '0.85rem', color: '#666' }}>
          <summary>Permissions: requires "{permissions.view}"</summary>
          <pre>{JSON.stringify(permissions, null, 2)}</pre>
        </details>
      )}

      {/* Toolbar */}
      {toolbarActions.length > 0 && (
        <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
          {toolbarActions.map((action: any) => (
            <button key={action.name} onClick={() => handleToolbarAction(action)}
                    style={{ padding: '0.5rem 1rem', cursor: 'pointer' }}
                    title={action.permission ? `Requires: ${action.permission}` : ''}>
              {action.icon && <span>[{action.icon}] </span>}
              {action.label}
              {action.selectionMode === 'multiple' && selectedRows.size > 0 &&
                ` (${selectedRows.size})`}
            </button>
          ))}
        </div>
      )}

      {/* Table */}
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            {hasMultiSelectAction && (
              <th style={{ padding: '0.5rem', borderBottom: '2px solid #ddd', width: '2rem' }}></th>
            )}
            {visibleColumns.map((col: any) => (
              <th key={col.name} style={{ padding: '0.5rem', borderBottom: '2px solid #ddd', textAlign: 'left' }}>
                {col.label ?? col.name}
                {col.computed && <span style={{ color: '#999' }}> (calc)</span>}
              </th>
            ))}
            {(rowActions.length > 0 || children.length > 0) && (
              <th style={{ padding: '0.5rem', borderBottom: '2px solid #ddd' }}>Actions</th>
            )}
          </tr>
        </thead>
        <tbody>
          {data.map((row, i) => (
            <tr key={i} style={{ borderBottom: '1px solid #eee',
                background: selectedRows.has(i) ? '#e8f0fe' : 'transparent' }}>
              {hasMultiSelectAction && (
                <td style={{ padding: '0.5rem' }}>
                  <input type="checkbox" checked={selectedRows.has(i)}
                         onChange={() => toggleRow(i)} />
                </td>
              )}
              {visibleColumns.map((col: any) => (
                <td key={col.name} style={{ padding: '0.5rem', ...getCellStyle(col, row[col.name]) }}>
                  {String(row[col.name] ?? '')}
                </td>
              ))}
              {(rowActions.length > 0 || children.length > 0) && (
                <td style={{ padding: '0.5rem' }}>
                  {rowActions.map((action: any) => (
                    <button key={action.name} onClick={() => handleRowAction(action, row)}
                            style={{ marginRight: '0.25rem', padding: '0.25rem 0.5rem', cursor: 'pointer' }}>
                      {action.label}
                    </button>
                  ))}
                  {children.map((child: any) => (
                    <button key={child.target} onClick={() => handleDrillDown(child, row)}
                            style={{ marginRight: '0.25rem', padding: '0.25rem 0.5rem',
                                     cursor: 'pointer', borderStyle: 'dashed' }}>
                      {child.label} →
                    </button>
                  ))}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>

      {/* Action log */}
      {actionLog.length > 0 && (
        <div style={{ marginTop: '1rem' }}>
          <h4>Action Log</h4>
          <ul>{actionLog.map((msg, i) => <li key={i}>{msg}</li>)}</ul>
        </div>
      )}
    </div>
  );
}
