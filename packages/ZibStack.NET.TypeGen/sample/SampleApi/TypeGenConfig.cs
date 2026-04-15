using ZibStack.NET.TypeGen;

namespace SampleApi;

/// <summary>
/// Demonstrates the fluent configurator DSL. The generator parses this method's
/// syntax at compile time — nothing runs at runtime. Arguments must be literal
/// expressions (string literals, enum members, constants). Class/property
/// attributes on individual DTOs still win over any setting configured here.
/// </summary>
public sealed class TypeGenConfig : ITypeGenConfigurator
{
    public void Configure(ITypeGenBuilder b)
    {
        b.TypeScript(ts =>
        {
            ts.PropertyNameStyle = NameStyle.CamelCase;
            ts.UseInterfaces = true;
        });

        b.OpenApi(oa =>
        {
            oa.Title = "Sample Order API";
            oa.Version = "1.2.3";
            oa.Description = "Demo service exercising the TypeGen fluent configurator.";
        });
    }
}
