using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class CrudApiInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    public string Route { get; }
    public string KeyPropertyName { get; }
    public string KeyTypeName { get; }
    public int Operations { get; }
    public int Style { get; }
    public string? AuthorizePolicy { get; }

    public string? CreateRequestName { get; }
    public string? UpdateRequestName { get; }
    public string? ResponseName { get; }
    public string? QueryName { get; }
    public bool IsCombinedDto { get; }
    public bool HasResponseDto { get; }
    public bool HasQueryDto { get; }

    public CrudApiInfo(
        string className,
        string? ns,
        string fullyQualifiedName,
        string route,
        string keyPropertyName,
        string keyTypeName,
        int operations,
        int style,
        string? authorizePolicy,
        string? createRequestName,
        string? updateRequestName,
        string? responseName,
        string? queryName,
        bool isCombinedDto,
        bool hasResponseDto,
        bool hasQueryDto)
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        Route = route;
        KeyPropertyName = keyPropertyName;
        KeyTypeName = keyTypeName;
        Operations = operations;
        Style = style;
        AuthorizePolicy = authorizePolicy;
        CreateRequestName = createRequestName;
        UpdateRequestName = updateRequestName;
        ResponseName = responseName;
        QueryName = queryName;
        IsCombinedDto = isCombinedDto;
        HasResponseDto = hasResponseDto;
        HasQueryDto = hasQueryDto;
    }
}
