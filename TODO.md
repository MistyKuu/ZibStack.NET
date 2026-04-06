# TODO

## ZibStack.NET.Dto — EF Core

- [ ] Consider extracting EF Core support into a separate package (`ZibStack.NET.Dto.EfCore` or similar) instead of conditional emission in the main generator
- [ ] Instead of generating `IEntityTypeConfiguration` / model builder code directly, convert validation attributes (e.g. `[MaxLength]`, `[Required]`) into EF Core-compatible attributes or Fluent API calls — let EF Core handle schema generation from those rather than duplicating the logic
