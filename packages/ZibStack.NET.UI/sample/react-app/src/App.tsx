import { DynamicForm } from './components/DynamicForm';
import { DynamicTable } from './components/DynamicTable';

/**
 * Sample app demonstrating ZibStack.NET.UI with React.
 *
 * Prerequisites:
 *   1. Run the SampleApi backend: `dotnet run --project ../SampleApi`
 *   2. The API serves form/table schemas at /api/forms/player and /api/tables/player
 */

// Example data for the table demo
const samplePlayers = [
  { id: 1, name: 'Alice',   level: 42, role: 'Admin',     createdAt: '2024-01-15', email: 'alice@example.com' },
  { id: 2, name: 'Bob',     level: 17, role: 'Player',    createdAt: '2024-03-22', email: 'bob@example.com' },
  { id: 3, name: 'Charlie', level: 88, role: 'Moderator', createdAt: '2024-06-01', email: 'charlie@example.com' },
];

export default function App() {
  return (
    <div style={{ maxWidth: '800px', margin: '2rem auto', fontFamily: 'system-ui, sans-serif' }}>
      <h1>ZibStack.NET.UI — React Sample</h1>

      <section>
        <h2>Dynamic Form (Create Mode)</h2>
        <p>
          This form is rendered from the JSON schema served by <code>Player.GetFormSchemaJson()</code>.
          Select "Admin" as the role to see conditional field visibility in action.
        </p>
        <DynamicForm
          schemaUrl="/api/forms/player"
          mode="create"
          onSubmit={(data) => {
            console.log('Form submitted:', data);
            alert('Submitted! Check console for values.');
          }}
        />
      </section>

      <hr style={{ margin: '3rem 0' }} />

      <section>
        <h2>Dynamic Table</h2>
        <p>
          This table is rendered from <code>Player.GetTableSchemaJson()</code>.
          Click column headers to sort. Use filter inputs to filter.
        </p>
        <DynamicTable
          schemaUrl="/api/tables/player"
          data={samplePlayers}
          totalCount={samplePlayers.length}
          onQueryChange={(q) => console.log('Table query changed:', q)}
        />
      </section>
    </div>
  );
}
