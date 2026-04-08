using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class CrudImpliedDtos
{
    public List<DtoClassInfo> CreateDtos { get; } = new();
    public List<DtoClassInfo> UpdateDtos { get; } = new();
    public List<ResponseDtoInfo> ResponseDtos { get; } = new();
}
