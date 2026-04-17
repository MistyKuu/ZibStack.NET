// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
	site: 'https://mistykuu.github.io',
	base: '/ZibStack.NET',
	integrations: [
		starlight({
			title: 'ZibStack.NET',
			logo: { dark: './src/assets/logo-dark.svg', light: './src/assets/logo-light.svg', replacesTitle: false },
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/MistyKuu/ZibStack.NET' }],
			editLink: { baseUrl: 'https://github.com/MistyKuu/ZibStack.NET/edit/master/docs/' },
			customCss: ['./src/styles/custom.css'],
			sidebar: [
				{ label: 'Getting Started', slug: 'getting-started' },
				{ label: 'Live Playground', link: 'https://zibstack-net.onrender.com/index.html', attrs: { target: '_blank' } },
				{
					label: 'Drop-in Attributes',
					items: [
						{
							label: 'Log — Structured Logging',
							items: [
								{ label: 'Overview', slug: 'packages/log' },
								{ label: 'Features', slug: 'packages/log/features' },
								{ label: 'Internals (rewriter)', slug: 'packages/log/internals' },
								{ label: '[Log] attribute & diagnostics', slug: 'packages/log/log-attribute' },
								{ label: 'Attribute reference', slug: 'packages/log/attributes' },
								{ label: 'Benchmarks', slug: 'packages/log/benchmarks' },
								{ label: 'Alternatives', slug: 'packages/log/comparison' },
							],
						},
						{
							label: 'AOP — Aspects & [Trace]',
							items: [
								{ label: 'Overview', slug: 'packages/aop' },
								{ label: 'Built-in aspects', slug: 'packages/aop/built-in' },
								{ label: 'Custom aspects & internals', slug: 'packages/aop/custom' },
								{ label: 'Alternatives', slug: 'packages/aop/comparison' },
							],
						},
						{ label: 'AOP Analyzers — Compile-Time Diagnostics', slug: 'packages/aop-analyzers' },
					],
				},
				{
					label: 'Utilities',
					items: [
						{ label: 'Core — Relations & Utility Types', slug: 'packages/core' },
						{
							label: 'Validation — Compile-Time Rules',
							items: [
								{ label: 'Overview', slug: 'packages/validation' },
								{ label: 'Alternatives', slug: 'packages/validation/comparison' },
							],
						},
						{
							label: 'Result — Functional Errors',
							items: [
								{ label: 'Overview', slug: 'packages/result' },
								{ label: 'Alternatives', slug: 'packages/result/comparison' },
							],
						},
						{
							label: 'TypeGen — TS & OpenAPI from C#',
							items: [
								{ label: 'Overview', slug: 'packages/typegen' },
								{ label: 'Type mapping', slug: 'packages/typegen/type-mapping' },
								{ label: 'Configuration & output', slug: 'packages/typegen/configuration' },
								{ label: 'Diagnostic reference', slug: 'packages/typegen/diagnostics' },
								{ label: 'Validation → OpenAPI', slug: 'packages/typegen/validation-mapping' },
								{ label: 'Endpoint discovery', slug: 'packages/typegen/endpoint-discovery' },
								{ label: 'Polymorphism & interfaces', slug: 'packages/typegen/polymorphism-and-interfaces' },
								{ label: 'Advanced type features', slug: 'packages/typegen/advanced-types' },
								{ label: 'Python emitter', slug: 'packages/typegen/emitters/python' },
								{ label: 'Zod emitter', slug: 'packages/typegen/emitters/zod' },
								{ label: 'GraphQL emitter', slug: 'packages/typegen/emitters/graphql' },
								{ label: 'Alternatives', slug: 'packages/typegen/comparison' },
							],
						},
					],
				},
				{
					label: 'CRUD Stack',
					items: [
						{
							label: 'Dto — CRUD API & DTOs',
							items: [
								{ label: 'Overview', slug: 'packages/dto' },
								{ label: 'Attributes reference', slug: 'packages/dto/attributes' },
								{ label: 'Fluent IDtoConfigurator', slug: 'packages/dto/fluent-config' },
								{ label: 'External types (DtoFor)', slug: 'packages/dto/external-types' },
								{ label: 'Utility types', slug: 'packages/dto/utility-types' },
								{ label: 'Query DTO', slug: 'packages/dto/querydto' },
								{ label: 'PaginatedResponse', slug: 'packages/dto/paginated' },
								{ label: 'CRUD API', slug: 'packages/dto/crud-api' },
								{ label: 'Response & mapping', slug: 'packages/dto/response-mapping' },
								{ label: 'JSON & validation', slug: 'packages/dto/json-and-validation' },
								{ label: 'Generated tests', slug: 'packages/dto/testing' },
								{ label: 'Alternatives', slug: 'packages/dto/comparison' },
							],
						},
						{ label: 'Query — Filter & Sort DSL', slug: 'packages/query' },
						{
							label: 'UI — Form & Table Metadata',
							items: [
								{ label: 'Overview', slug: 'packages/ui' },
								{ label: 'Generated JSON schemas', slug: 'packages/ui/json-schemas' },
								{ label: 'Relationships', slug: 'packages/ui/relationships' },
								{ label: 'Database integration', slug: 'packages/ui/database' },
								{ label: 'Frontend integration', slug: 'packages/ui/frontend' },
								{ label: 'All attributes', slug: 'packages/ui/attributes' },
							],
						},
						{
							label: 'Storage Adapters',
							collapsed: false,
							items: [
								{ label: 'EF Core', slug: 'packages/entityframework' },
								{ label: 'Dapper', slug: 'packages/dapper' },
							],
						},
					],
				},
				{
					label: 'Guides',
					items: [
						{ label: 'Full CRUD with SQLite', slug: 'guides/crud-sqlite' },
						{ label: 'Modeling Relationships & Query DSL', slug: 'guides/relationships-query-dsl' },
						{ label: 'Declarative Observability', slug: 'guides/observability' },
						{ label: 'PatchField Tri-State', slug: 'guides/patchfield-tri-state' },
					],
				},
			],
		}),
	],
});
