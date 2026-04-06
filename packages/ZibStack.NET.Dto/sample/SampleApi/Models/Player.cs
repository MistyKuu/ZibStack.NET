using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Sample.Models;

[CreateDto]
[UpdateDto]
[ResponseDto]
[QueryDto(Sortable = true, DefaultSort = "Name")]
[CrudApi(Style = ApiStyle.Both)]
public class Player
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }
    public Address? Address { get; set; }

    [CreateOnly]
    [ResponseIgnore]
    public required string Password { get; set; }

    [UpdateOnly]
    public string? DeactivationReason { get; set; }

    [DtoIgnore]
    public bool IsAdmin { get; set; }

    [DtoIgnore]
    public DateTime CreatedAt { get; set; }
}

[ResponseDto]
public class Address
{
    public required string Street { get; set; }
    public required string City { get; set; }
    public string? ZipCode { get; set; }
}
