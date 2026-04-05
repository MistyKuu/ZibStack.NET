namespace ZibStack.NET.Dto.Tests;

public class IntersectFromTests
{
    [Fact]
    public void IntersectFrom_IsRecord()
    {
        Assert.NotNull(typeof(CategoryWithAudit).GetMethod("<Clone>$"));
    }

    [Fact]
    public void IntersectFrom_HasPropertiesFromBothTypes()
    {
        var type = typeof(CategoryWithAudit);

        // From Category
        Assert.NotNull(type.GetProperty("Id"));
        Assert.NotNull(type.GetProperty("Name"));
        Assert.NotNull(type.GetProperty("Description"));
        Assert.NotNull(type.GetProperty("SortOrder"));

        // From Audit
        Assert.NotNull(type.GetProperty("ModifiedBy"));
        Assert.NotNull(type.GetProperty("ModifiedAt"));
    }

    [Fact]
    public void IntersectFrom_HasApplyToForCategory()
    {
        var methods = typeof(CategoryWithAudit).GetMethods()
            .Where(m => m.Name == "ApplyTo")
            .ToArray();

        Assert.Contains(methods, m => m.GetParameters()[0].ParameterType == typeof(Category));
    }

    [Fact]
    public void IntersectFrom_HasApplyToForAudit()
    {
        var methods = typeof(CategoryWithAudit).GetMethods()
            .Where(m => m.Name == "ApplyTo")
            .ToArray();

        Assert.Contains(methods, m => m.GetParameters()[0].ParameterType == typeof(Audit));
    }

    [Fact]
    public void IntersectFrom_ApplyTo_Category()
    {
        var category = new Category
        {
            Id = 1,
            Name = "Old",
            Description = "Old desc",
            SortOrder = 1
        };

        var intersect = new CategoryWithAudit { Name = "New", SortOrder = 5 };
        intersect.ApplyTo(category);

        Assert.Equal("New", category.Name);
        Assert.Equal(5, category.SortOrder);
        Assert.Equal("Old desc", category.Description);  // unchanged
    }

    [Fact]
    public void IntersectFrom_ApplyTo_Audit()
    {
        var audit = new Audit
        {
            ModifiedBy = "old_user",
            ModifiedAt = new DateTime(2020, 1, 1)
        };

        var now = DateTime.UtcNow;
        var intersect = new CategoryWithAudit { ModifiedBy = "new_user", ModifiedAt = now };
        intersect.ApplyTo(audit);

        Assert.Equal("new_user", audit.ModifiedBy);
        Assert.Equal(now, audit.ModifiedAt);
    }

    [Fact]
    public void IntersectFrom_DeduplicatesProperties()
    {
        // If both types had a property with the same name,
        // only one PatchField should be generated (first wins)
        var type = typeof(CategoryWithAudit);
        var props = type.GetProperties().Where(p => p.Name == "Name").ToArray();
        Assert.Single(props);
    }
}
