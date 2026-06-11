using ZibStack.NET.Dto;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

// Optimistic concurrency demo: the generator adds a RowVersion property, GET/POST/PATCH
// responses carry an ETag header, PATCH requires If-Match (428/412) and DELETE honors it.
[CrudApi(Concurrency = true)]
public partial class Document
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }

    [ZRequired] [ZMinLength(1)] [ZMaxLength(200)]
    public required string Title { get; set; }

    public string? Content { get; set; }
}
