using ZibStack.NET.Core;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace SampleApi.Models;

// ─── One attribute generates everything: CRUD API + DTOs + Form + Table + EF Config + Validation + Audit ───

[ImTiredOfCrud(DefaultSort = "Name")]
public partial class Team
{
    public int Id { get; set; }

    [Required] [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string? City { get; set; }

    public int FoundedYear { get; set; }

    [OneToMany(Label = "Players")]
    public ICollection<Player> Players { get; set; } = new List<Player>();
}
