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
    public string? GetByIdPolicy { get; }
    public string? GetListPolicy { get; }
    public string? CreatePolicy { get; }
    public string? UpdatePolicy { get; }
    public string? DeletePolicy { get; }

    public string? CreateRequestName { get; }
    public string? UpdateRequestName { get; }
    public string? ResponseName { get; }
    public string? ListResponseName { get; }
    public string? QueryName { get; }
    public Dictionary<string, string> ColumnPermissions { get; }
    public bool IsCombinedDto { get; }
    public bool HasResponseDto { get; }
    public bool HasQueryDto { get; }
    public bool HasQueryDsl { get; set; }

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
        bool hasQueryDto,
        string? getByIdPolicy = null,
        string? getListPolicy = null,
        string? createPolicy = null,
        string? updatePolicy = null,
        string? deletePolicy = null,
        string? listResponseName = null,
        Dictionary<string, string>? columnPermissions = null)
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
        GetByIdPolicy = getByIdPolicy;
        GetListPolicy = getListPolicy;
        CreatePolicy = createPolicy;
        UpdatePolicy = updatePolicy;
        DeletePolicy = deletePolicy;
        CreateRequestName = createRequestName;
        UpdateRequestName = updateRequestName;
        ResponseName = responseName;
        ListResponseName = listResponseName;
        ColumnPermissions = columnPermissions ?? new Dictionary<string, string>();
        QueryName = queryName;
        IsCombinedDto = isCombinedDto;
        HasResponseDto = hasResponseDto;
        HasQueryDto = hasQueryDto;
    }
}
