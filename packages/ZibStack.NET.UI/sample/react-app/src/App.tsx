import { DynamicForm } from './components/DynamicForm';
import { DynamicTable } from './components/DynamicTable';
import { ErpTable } from './components/ErpTable';

/**
 * Sample app demonstrating ZibStack.NET.UI with React.
 *
 * Prerequisites:
 *   1. Run the SampleApi backend: `dotnet run --project ../SampleApi`
 *   2. The API serves form/table schemas at /api/forms/player and /api/tables/player
 */

const samplePlayers = [
  { id: 1, name: 'Alice',   level: 42, role: 'Admin',     createdAt: '2024-01-15', email: 'alice@example.com' },
  { id: 2, name: 'Bob',     level: 17, role: 'Player',    createdAt: '2024-03-22', email: 'bob@example.com' },
  { id: 3, name: 'Charlie', level: 88, role: 'Moderator', createdAt: '2024-06-01', email: 'charlie@example.com' },
];

const sampleVoivodeships = [
  { id: 1, name: 'Wielkopolskie', code: 'WP', capital: 'Poznań',   budget: 1250000, countyCount: 35, population: 3498733 },
  { id: 2, name: 'Mazowieckie',  code: 'MZ', capital: 'Warszawa',  budget: -500000, countyCount: 42, population: 5411446 },
  { id: 3, name: 'Małopolskie',  code: 'MA', capital: 'Kraków',    budget: 750000,  countyCount: 22, population: 3400577 },
];

export default function App() {
  return (
    <div style={{ maxWidth: '900px', margin: '2rem auto', fontFamily: 'system-ui, sans-serif' }}>
      <h1>ZibStack.NET.UI — React Sample</h1>

      <section>
        <h2>Dynamic Form (Create Mode)</h2>
        <p>
          This form is rendered from <code>Player.GetFormSchemaJson()</code>.
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

      <hr style={{ margin: '3rem 0' }} />

      <section>
        <h2>ERP Table — Voivodeships</h2>
        <p>
          Full ERP-style table from <code>VoivodeshipView.GetTableSchemaJson()</code>.
          Features: <strong>toolbar actions</strong> (Eksport, Przelicz salda),{' '}
          <strong>row actions</strong> (Szczegóły, Raport),{' '}
          <strong>drill-down</strong> (Powiaty →, Kody pocztowe →),{' '}
          <strong>computed columns</strong> with <strong>conditional styling</strong> (Budget: red when negative, green when positive),{' '}
          <strong>permission metadata</strong>, and <strong>row selection</strong> for multi-select actions.
        </p>
        <ErpTable
          schemaUrl="/api/tables/voivodeship"
          data={sampleVoivodeships}
          totalCount={sampleVoivodeships.length}
          onDrillDown={(target, fk, parentId) =>
            console.log(`Navigate to ${target} where ${fk}=${parentId}`)
          }
        />
      </section>
    </div>
  );
}
