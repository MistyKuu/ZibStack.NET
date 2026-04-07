using System.ComponentModel.DataAnnotations;
using ZibStack.NET.Dto;
using ZibStack.NET.Utils;

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

    [MinLength(2)]
    [MaxLength(50)]
    public required string Name { get; set; }

    [Range(1, 100)]
    public int Level { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    public Address? Address { get; set; }

    [CreateOnly]
    [ResponseIgnore]
    [MinLength(8)]
    public required string Password { get; set; }

    [UpdateOnly]
    [MaxLength(500)]
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
