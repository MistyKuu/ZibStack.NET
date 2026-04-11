using ZibStack.NET.Dto;

// SDTO012 is suppressed project-wide in ZibStack.NET.Dto.Tests.csproj — these
// [CrudApi] fixtures are inspected via reflection, never wired into a real DI
// container or ASP.NET host, so the "no ICrudStore registered" warning is noise.

namespace ZibStack.NET.Dto.Tests;

[CrudApi(Route = "api/items", KeyProperty = "Id", Operations = CrudOperations.Read)]
[CreateDto]
[UpdateDto]
[ResponseDto]
public class CrudItem
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Name { get; set; }
    public decimal Price { get; set; }
}

[CrudApi]
[CreateOrUpdateDto]
[ResponseDto]
public class CrudWidget
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Label { get; set; }
    public int Count { get; set; }
}

public class CrudApiTests
{
    [Fact]
    public void CrudApiAttribute_Exists()
    {
        var attr = typeof(CrudItem).GetCustomAttributes(false)
            .FirstOrDefault(a => a.GetType().Name == "CrudApiAttribute");
        Assert.NotNull(attr);
    }

    [Fact]
    public void CrudApiAttribute_HasRouteProperty()
    {
        var attr = typeof(CrudItem).GetCustomAttributes(false)
            .First(a => a.GetType().Name == "CrudApiAttribute");
        var routeProp = attr.GetType().GetProperty("Route");
        Assert.NotNull(routeProp);
        Assert.Equal("api/items", routeProp.GetValue(attr));
    }

    [Fact]
    public void CrudApiAttribute_HasKeyProperty()
    {
        var attr = typeof(CrudItem).GetCustomAttributes(false)
            .First(a => a.GetType().Name == "CrudApiAttribute");
        var keyProp = attr.GetType().GetProperty("KeyProperty");
        Assert.NotNull(keyProp);
        Assert.Equal("Id", keyProp.GetValue(attr));
    }

    [Fact]
    public void CrudApiAttribute_HasOperationsProperty()
    {
        var attr = typeof(CrudItem).GetCustomAttributes(false)
            .First(a => a.GetType().Name == "CrudApiAttribute");
        var opsProp = attr.GetType().GetProperty("Operations");
        Assert.NotNull(opsProp);
        var val = (int)opsProp.GetValue(attr)!;
        Assert.Equal((int)CrudOperations.Read, val);
    }

    [Fact]
    public void CrudOperations_FlagsWork()
    {
        Assert.Equal(1, (int)CrudOperations.GetById);
        Assert.Equal(2, (int)CrudOperations.GetList);
        Assert.Equal(4, (int)CrudOperations.Create);
        Assert.Equal(8, (int)CrudOperations.Update);
        Assert.Equal(16, (int)CrudOperations.Delete);
        Assert.Equal(3, (int)CrudOperations.Read);
        Assert.Equal(28, (int)CrudOperations.Write);
        Assert.Equal(31, (int)CrudOperations.All);
    }

    [Fact]
    public void ApiStyle_ValuesMatch()
    {
        Assert.Equal(0, (int)ApiStyle.MinimalApi);
        Assert.Equal(1, (int)ApiStyle.Controller);
        Assert.Equal(2, (int)ApiStyle.Both);
    }

    [Fact]
    public void ICrudStore_InterfaceExists()
    {
        var storeType = typeof(ICrudStore<,>);
        Assert.NotNull(storeType);
        Assert.True(storeType.IsInterface);
        Assert.Equal(2, storeType.GetGenericArguments().Length);
    }

    [Fact]
    public void ICrudStore_HasExpectedMethods()
    {
        var storeType = typeof(ICrudStore<CrudItem, int>);
        Assert.NotNull(storeType.GetMethod("GetByIdAsync"));
        Assert.NotNull(storeType.GetMethod("Query"));
        Assert.NotNull(storeType.GetMethod("CreateAsync"));
        Assert.NotNull(storeType.GetMethod("UpdateAsync"));
        Assert.NotNull(storeType.GetMethod("DeleteAsync"));
    }

    [Fact]
    public void CrudWidget_DefaultRoute_UsesPluralizedName()
    {
        var attr = typeof(CrudWidget).GetCustomAttributes(false)
            .First(a => a.GetType().Name == "CrudApiAttribute");
        var routeProp = attr.GetType().GetProperty("Route");
        // Defaults to null in attribute — generator handles pluralization
        Assert.Null(routeProp!.GetValue(attr));
    }

    [Fact]
    public void CrudItem_DtosStillGenerated()
    {
        // [CrudApi] doesn't break existing DTO generation
        Assert.NotNull(Type.GetType("ZibStack.NET.Dto.Tests.CreateCrudItemRequest, ZibStack.NET.Dto.Tests")
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "CreateCrudItemRequest"));
    }

    [Fact]
    public void CrudItem_ResponseDtoStillGenerated()
    {
        var responseType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "CrudItemResponse");
        Assert.NotNull(responseType);
    }

    [Fact]
    public void CrudWidget_CombinedDtoStillGenerated()
    {
        var requestType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "CrudWidgetRequest");
        Assert.NotNull(requestType);
    }
}
