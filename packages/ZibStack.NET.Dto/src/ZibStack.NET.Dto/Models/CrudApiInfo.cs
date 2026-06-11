using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class CrudApiInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    // Made settable so the fluent IDtoConfigurator pipeline can override values
    // pulled from the [CrudApi] attribute. Read-elsewhere unchanged.
    public string Route { get; set; }
    public string KeyPropertyName { get; set; }
    public string KeyTypeName { get; }
    public int Operations { get; set; }
    public int Style { get; set; }
    public string? AuthorizePolicy { get; set; }
    public string? GetByIdPolicy { get; set; }
    public string? GetListPolicy { get; set; }
    public string? CreatePolicy { get; set; }
    public string? UpdatePolicy { get; set; }
    public string? DeletePolicy { get; set; }

    public string? CreateRequestName { get; }
    public string? UpdateRequestName { get; }
    public string? ResponseName { get; }
    public string? ListResponseName { get; }
    public string? QueryName { get; }
    public Dictionary<string, string> ColumnPermissions { get; }
    /// <summary>Subset of <see cref="ColumnPermissions"/> whose columns survive in the list DTO
    /// (i.e. not removed by [DtoIgnore(DtoTarget.List)]). Used by the list endpoint.</summary>
    public Dictionary<string, string> ListColumnPermissions { get; set; } = new();
    public bool IsCombinedDto { get; }
    public bool HasResponseDto { get; }
    public bool HasQueryDto { get; }
    public bool HasQueryDsl { get; set; }
    public bool SoftDelete { get; set; }
    public bool SignalR { get; set; }

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
