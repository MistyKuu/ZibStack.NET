using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

public class GeoCoord
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public class Location
{
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public int PostalCode { get; set; }
    public GeoCoord? Geo { get; set; }
}

[ResponseDto]
public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    [Flatten]
    public Location? Location { get; set; }
}

public class FlattenTests
{
    [Fact]
    public void Flatten_ExpandsChildProperties()
    {
        var type = typeof(StoreResponse);
        Assert.NotNull(type.GetProperty("LocationCity"));
        Assert.NotNull(type.GetProperty("LocationCountry"));
        Assert.NotNull(type.GetProperty("LocationPostalCode"));
    }

    [Fact]
    public void Flatten_OriginalPropertyRemoved()
    {
        var type = typeof(StoreResponse);
        Assert.Null(type.GetProperty("Location"));
    }

    [Fact]
    public void Flatten_NullableParent_ChildrenNullable()
    {
        // Location is nullable, so LocationCity should be string? and LocationPostalCode should be int?
        var type = typeof(StoreResponse);
        var cityProp = type.GetProperty("LocationCity")!;
        Assert.True(cityProp.PropertyType == typeof(string)); // string is already nullable ref

        var postalProp = type.GetProperty("LocationPostalCode")!;
        Assert.True(Nullable.GetUnderlyingType(postalProp.PropertyType) == typeof(int));
    }

    [Fact]
    public void Flatten_FromEntity_MapsChildProperties()
    {
        var store = new Store
        {
            Id = 1,
            Name = "Main Store",
            Location = new Location { City = "NYC", Country = "US", PostalCode = 10001 }
        };

        var response = StoreResponse.FromEntity(store);

        Assert.Equal(1, response.Id);
        Assert.Equal("Main Store", response.Name);
        Assert.Equal("NYC", response.LocationCity);
        Assert.Equal("US", response.LocationCountry);
        Assert.Equal(10001, response.LocationPostalCode);
    }

    [Fact]
    public void Flatten_FromEntity_NullParent_ChildrenNull()
    {
        var store = new Store { Id = 1, Name = "Empty", Location = null };

        var response = StoreResponse.FromEntity(store);

        Assert.Null(response.LocationCity);
        Assert.Null(response.LocationCountry);
        Assert.Null(response.LocationPostalCode);
    }

    // ─── Deep recursive flatten ────────────────────────────────────

    [Fact]
    public void Flatten_DeepRecursive_ExpandsAllLevels()
    {
        var type = typeof(StoreResponse);
        // GeoCoord is nested inside Location → should be flattened as LocationGeoLat, LocationGeoLng
        Assert.NotNull(type.GetProperty("LocationGeoLat"));
        Assert.NotNull(type.GetProperty("LocationGeoLng"));
    }

    [Fact]
    public void Flatten_DeepRecursive_FromEntity_Maps()
    {
        var store = new Store
        {
            Id = 1,
            Name = "GeoStore",
            Location = new Location
            {
                City = "NYC",
                Country = "US",
                PostalCode = 10001,
                Geo = new GeoCoord { Lat = 40.7, Lng = -74.0 }
            }
        };

        var response = StoreResponse.FromEntity(store);

        Assert.Equal(40.7, response.LocationGeoLat);
        Assert.Equal(-74.0, response.LocationGeoLng);
    }

    [Fact]
    public void Flatten_DeepRecursive_NullMiddle_ReturnsNull()
    {
        var store = new Store
        {
            Id = 1,
            Name = "NoGeo",
            Location = new Location { City = "X", Country = "Y", PostalCode = 1, Geo = null }
        };

        var response = StoreResponse.FromEntity(store);

        Assert.Null(response.LocationGeoLat);
        Assert.Null(response.LocationGeoLng);
    }
}
