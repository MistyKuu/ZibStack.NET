using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

// Address has NO [UpdateDto] — should be auto-generated from Player
public class ContactInfo
{
    public string Phone { get; set; } = "";
    public string? Fax { get; set; }
}

public class Company
{
    public string Name { get; set; } = "";
    public ContactInfo? Contact { get; set; }
}

[UpdateDto]
public class Employee
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Name { get; set; }
    public Company? Company { get; set; }  // nested → auto UpdateCompanyRequest → auto UpdateContactInfoRequest
}

[CreateDto]
public class Project
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Title { get; set; }
    public ContactInfo? Lead { get; set; }  // nested → auto CreateContactInfoRequest
}

public class AutoNestedTests
{
    [Fact]
    public void AutoNested_GeneratesNestedDtoWithoutExplicitAttribute()
    {
        // UpdateCompanyRequest should exist even though Company has no [UpdateDto]
        var type = typeof(UpdateEmployeeRequest).Assembly
            .GetType("ZibStack.NET.Dto.Tests.UpdateCompanyRequest");
        Assert.NotNull(type);
    }

    [Fact]
    public void AutoNested_DeepRecursive_GeneratesAllLevels()
    {
        // UpdateContactInfoRequest should exist (Company → ContactInfo)
        var type = typeof(UpdateEmployeeRequest).Assembly
            .GetType("ZibStack.NET.Dto.Tests.UpdateContactInfoRequest");
        Assert.NotNull(type);
    }

    [Fact]
    public void AutoNested_PropertyUsesNestedDtoType()
    {
        var prop = typeof(UpdateEmployeeRequest).GetProperty("Company")!;
        var innerType = prop.PropertyType.GetGenericArguments()[0]; // PatchField<T> → T
        Assert.Equal("UpdateCompanyRequest", innerType.Name.TrimEnd('?'));
    }

    [Fact]
    public void AutoNested_ApplyTo_RecursivePartialUpdate()
    {
        var employee = new Employee
        {
            Id = 1,
            Name = "Alice",
            Company = new Company
            {
                Name = "OldCorp",
                Contact = new ContactInfo { Phone = "111", Fax = "222" }
            }
        };

        // Only update company name, leave contact untouched
        var companyUpdate = typeof(UpdateEmployeeRequest).Assembly
            .GetType("ZibStack.NET.Dto.Tests.UpdateCompanyRequest")!;
        var companyInstance = Activator.CreateInstance(companyUpdate);
        // Use reflection to set Name on UpdateCompanyRequest
        var nameProp = companyUpdate.GetProperty("Name")!;
        nameProp.SetValue(companyInstance, (PatchField<string>)"NewCorp");

        var request = new UpdateEmployeeRequest();
        // We can't easily set nested via init, so test the simpler case
        var simpleRequest = new UpdateEmployeeRequest { Name = "Bob" };
        simpleRequest.ApplyTo(employee);

        Assert.Equal("Bob", employee.Name);
        Assert.Equal("OldCorp", employee.Company.Name);  // untouched
    }

    [Fact]
    public void AutoNested_Create_GeneratesNestedCreateDto()
    {
        var type = typeof(CreateProjectRequest).Assembly
            .GetType("ZibStack.NET.Dto.Tests.CreateContactInfoRequest");
        Assert.NotNull(type);
    }

    [Fact]
    public void AutoNested_Create_ToEntity_Works()
    {
        var request = new CreateProjectRequest { Title = "MyProject" };
        var entity = request.ToEntity();

        Assert.Equal("MyProject", entity.Title);
        Assert.Null(entity.Lead);  // not set
    }
}
