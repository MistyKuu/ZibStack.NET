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
    public void IntersectFrom_ApplyTo_Category_OverwritesAllFields()
    {
        // [IntersectFrom] is a structural composition, not a partial update.
        // ApplyTo unconditionally writes every property of the source — to keep
        // existing target values you must hydrate the intersect from the target
        // first (or use FromEntity).
        var category = new Category
        {
            Id = 1,
            Name = "Old",
            Description = "Old desc",
            SortOrder = 1
        };

        var intersect = CategoryWithAudit.FromEntity(category) with
        {
            Name = "New",
            SortOrder = 5,
        };
        intersect.ApplyTo(category);

        Assert.Equal("New", category.Name);
        Assert.Equal(5, category.SortOrder);
        Assert.Equal("Old desc", category.Description); // preserved via FromEntity hydration
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
    public void IntersectFrom_FromEntity_PerSource()
    {
        // FromEntity is generated once per source type and copies that source's properties.
        var category = new Category { Id = 1, Name = "Cat", Description = "Desc", SortOrder = 7 };
        var fromCat = CategoryWithAudit.FromEntity(category);
        Assert.Equal(1, fromCat.Id);
        Assert.Equal("Cat", fromCat.Name);
        Assert.Equal("Desc", fromCat.Description);
        Assert.Equal(7, fromCat.SortOrder);
    }

    [Fact]
    public void IntersectFrom_DeduplicatesProperties()
    {
        // If both types had a property with the same name,
        // only one property should be generated (first source wins)
        var type = typeof(CategoryWithAudit);
        var props = type.GetProperties().Where(p => p.Name == "Name").ToArray();
        Assert.Single(props);
    }
}
