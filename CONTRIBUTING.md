# Contributing to ZibStack.NET

Thanks for taking the time to contribute! ZibStack.NET is a collection of Roslyn
source generators and utilities, so most changes touch either a generator, its
analyzer, or the generated-code shape — small, well-tested PRs are the easiest to
review and merge.

## Ways to contribute

- **Report a bug** — open an [issue](https://github.com/MistyKuu/ZibStack.NET/issues)
  with a minimal repro (the model/attribute you used + what was generated vs. what
  you expected). The [Playground](https://zibstack-playground-808858943057.europe-central2.run.app/index.html)
  is a quick way to capture generated output.
- **Suggest a feature** — open an issue describing the use case before writing code,
  especially for new attributes or generator behavior, so we can agree on the shape first.
- **Send a pull request** — fix a bug, add a test, improve docs, or implement an
  agreed-upon feature.

You don't need write access: **fork the repo, push a branch to your fork, and open a
PR against `master`.**

## Prerequisites

- **.NET 10 SDK** (`10.0.x`) — CI builds and tests against it.
- A Roslyn-aware IDE helps: Visual Studio 2022+, Rider, or VS Code with C# Dev Kit.

## Project layout

```
packages/
  ZibStack.NET.<Name>/
    src/      generator + abstractions
    tests/    xUnit tests (and analyzer tests where applicable)
    sample/   runnable sample app exercising the generator
docs/         Astro/Starlight documentation site
ZibStack.NET.slnx   solution (MSBuild .slnx format)
```

Packages: **Aop** (+ Polly, HybridCache add-ons), **Core**, **Dapper**, **Dto**,
**EntityFramework**, **Log**, **Query**, **Result**, **TypeGen**, **UI**, **Validation**.

## Build & test

```bash
# Build everything
dotnet build ZibStack.NET.slnx -c Release

# Run a single package's tests (matches how CI runs them)
dotnet test packages/ZibStack.NET.Dto/tests/ZibStack.NET.Dto.Tests/ZibStack.NET.Dto.Tests.csproj -c Release
```

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds the solution and
runs each package's test project on every push and pull request to `master`. Run the
relevant test project locally before opening a PR.

> **Source-generator gotcha:** if generated code looks stale after an edit, the
> Roslyn build-server may be caching the old generator. Run `dotnet build-server
> shutdown` (on its own, not chained with other commands) and rebuild, or restart
> your IDE.

Benchmarks are intentionally **not** in CI — run them locally when touching hot paths:

```bash
dotnet run --project packages/ZibStack.NET.Log/tests/ZibStack.NET.Log.Benchmarks -c Release
dotnet run --project packages/ZibStack.NET.Aop/tests/ZibStack.NET.Aop.Benchmarks -c Release
```

## Pull request guidelines

1. **Branch from `master`** and keep the PR focused on one change.
2. **Add or update tests.** Generator changes should assert on generated behavior
   (the sample apps under `sample/` host integration tests via
   `WebApplicationFactory`); analyzer changes need analyzer tests.
3. **Update the docs.** New packages or user-facing features need a page (or section)
   under `docs/src/content/docs/packages/`, and new packages/features should be added
   to the root `README.md`.
4. **Keep the build clean** — no new warnings.
5. **Fill in the PR template** so reviewers can see what changed and how it was verified.

### Commit messages

Prefix the subject with the affected package in brackets, matching the existing
history:

```
[Dto]: add cursor pagination to generated GET list endpoints
[Aop]: convert aspect failures to failed Results on Result-returning methods
[Multi]: <message>      # cross-package change
fix: <message>          # repo-wide / tooling fix
```

Keep the subject imperative and under ~72 characters; put the why in the body.

## Reporting security issues

Please **don't** open a public issue for security-sensitive reports. Contact the
maintainer directly (see the GitHub profile) so it can be addressed before disclosure.

## Code of conduct

Be respectful and constructive. We want this to be a welcoming project to contribute to.
