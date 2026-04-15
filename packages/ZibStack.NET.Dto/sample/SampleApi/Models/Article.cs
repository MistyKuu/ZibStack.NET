using System;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

// Zero Dto attributes on the class — Create/Update DTOs come from the fluent
// IDtoConfigurator (DtoConfig.cs). Per-property validation attributes still
// apply (locality wins for those — see DtoConfig.cs notes).
public partial class Article
{
    public int Id { get; set; }

    [ZRequired] [ZMaxLength(200)]
    public required string Title { get; set; }

    [ZMaxLength(10_000)]
    public string? Body { get; set; }

    public DateTime PublishedAt { get; set; }
}
