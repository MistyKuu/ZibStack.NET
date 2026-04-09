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
					label: 'Packages',
					items: [
						{ label: 'Dto — CRUD API & DTOs', slug: 'packages/dto' },
						{ label: 'EntityFramework', slug: 'packages/entityframework' },
						{ label: 'Dapper', slug: 'packages/dapper' },
						{ label: 'Log', slug: 'packages/log' },
						{ label: 'Aop', slug: 'packages/aop' },
						{ label: 'Validation', slug: 'packages/validation' },
						{ label: 'Core', slug: 'packages/core' },
						{ label: 'Query — Filter & Sort DSL', slug: 'packages/query' },
						{ label: 'Result', slug: 'packages/result' },
						{ label: 'UI', slug: 'packages/ui' },
					],
				},
				{
					label: 'Guides',
					items: [
						{ label: 'Full CRUD with SQLite', slug: 'guides/crud-sqlite' },
					],
				},
			],
		}),
	],
});
