using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Sample.Models;

[CreateOrUpdateDto]
[ResponseDto]
[CrudApi(Style = ApiStyle.MinimalApi)]
public class Team
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public int MaxMembers { get; set; }

    [DtoIgnore]
    public DateTime CreatedAt { get; set; }
}
