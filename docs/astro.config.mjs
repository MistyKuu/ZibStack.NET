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
						{ label: 'Log — Structured Logging', slug: 'packages/log' },
						{ label: 'AOP — Aspects & [Trace]', slug: 'packages/aop' },
						{ label: 'AOP Analyzers — Compile-Time Diagnostics', slug: 'packages/aop-analyzers' },
					],
				},
				{
					label: 'Utilities',
					items: [
						{ label: 'Core — Relations & Utility Types', slug: 'packages/core' },
						{ label: 'Validation — Compile-Time Rules', slug: 'packages/validation' },
						{ label: 'Result — Functional Errors', slug: 'packages/result' },
					],
				},
				{
					label: 'CRUD Stack',
					items: [
						{ label: 'Dto — CRUD API & DTOs', slug: 'packages/dto' },
						{ label: 'Query — Filter & Sort DSL', slug: 'packages/query' },
						{ label: 'UI — Form & Table Metadata', slug: 'packages/ui' },
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
