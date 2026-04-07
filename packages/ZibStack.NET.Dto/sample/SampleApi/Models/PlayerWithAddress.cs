using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Sample.Models;

[IntersectFrom(typeof(Player))]
[IntersectFrom(typeof(Address))]
public partial record PlayerWithAddress;
