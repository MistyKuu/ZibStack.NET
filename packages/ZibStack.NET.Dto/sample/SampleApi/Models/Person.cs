using ZibStack.NET.Core;

namespace ZibStack.NET.Dto.Sample.Models;

/// <summary>
/// Source type for the <c>[Destructurable&lt;T&gt;]</c> demo. Plain record — no attributes here.
/// The shape(s) live as separate partial records below, each pointing back to this type.
/// </summary>
public record Person(
    string Name,
    int Id,
    string Email,
    int Age,
    string City);

/// <summary>
/// Shape: pick Name + Id. Generator emits a nested <c>Rest(Email, Age, City)</c> record,
/// <c>FromSource</c>, <c>RestOf</c> and <c>Split</c> factories.
/// </summary>
[Destructurable<Person>]
public partial record PersonNameId(string Name, int Id);

/// <summary>
/// Shape: pick Name only. Separate shape → separate (and reusable) DTO.
/// </summary>
[Destructurable<Person>]
public partial record PersonJustName(string Name);

/// <summary>
/// Shape with body-style properties instead of primary ctor. Generator falls back
/// to object-initializer construction — both styles are supported.
/// </summary>
[Destructurable<Person>]
public partial record PersonContact
{
    public required string Name { get; init; }
    public required string Email { get; init; }
}
