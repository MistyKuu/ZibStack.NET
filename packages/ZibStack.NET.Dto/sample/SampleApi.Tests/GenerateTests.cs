using ZibStack.NET.Dto;

// This single line triggers the Dto generator to scan referenced assemblies for
// [CrudApi] entities (Player, Team in SampleApi) and emit xUnit integration test
// classes with WebApplicationFactory<Program>, valid request bodies, and full CRUD
// cycle coverage. The generated tests auto-update on every build — don't edit them,
// write custom tests in separate files instead.
[assembly: GenerateCrudTests]
