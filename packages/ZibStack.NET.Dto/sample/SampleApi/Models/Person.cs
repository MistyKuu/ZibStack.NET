using ZibStack.NET.Core;

namespace ZibStack.NET.Dto.Sample.Models;

/// <summary>
/// Sample type demonstrating <c>[Destructurable]</c> — JS-style destructuring.
/// The source generator scans <c>PickXxx()</c> call sites and emits typed
/// extension methods + 'rest' types on demand. No combinatorial explosion:
/// only the combos you actually use are generated.
/// </summary>
[Destructurable]
public partial class Person
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public string City { get; set; } = "";
}
