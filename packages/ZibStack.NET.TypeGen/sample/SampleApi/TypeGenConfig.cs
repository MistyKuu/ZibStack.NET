using SampleApi.Models;
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
            ts.OutputDir = "generated";
        });

        b.OpenApi(oa =>
        {
            oa.Title = "Sample Order API";
            oa.Version = "1.2.3";
            oa.Description = "Demo service exercising the TypeGen fluent configurator.";
            // Set to false to emit schemas-only (no paths block). Useful when
            // Swashbuckle / another tool owns the paths and TypeGen just
            // provides the `components/schemas` part of the contract.
            // oa.EmitPaths = false;
        });

        b.Zod(z =>
        {
            z.OutputDir = "generated";
            z.EmitInferredTypes = true;
        });
        
        b.ForType<Root>()
            .WithGeneratedTypes(TypeTarget.TypeScript)
            .Property(r => r.El).UseType<Dupa>();

        b.ForType<OrderItem>().TsName("hoho");
        b.ForType<OrderItem>().Property(x => x.UnitPrice).TsName("ASD");

        // Per-type fluent overrides — equivalent to putting [TsName]/[OpenApiSchemaName]
        // attributes on Customer, but without touching the source file. Same precedence
        // rules apply: if Customer also had a [TsName] attribute it would win over this.
        b.ForType<Customer>()
            .TsName("CustomerDto")
            .OpenApiName("CustomerV1")
            .Property(c => c.Email)
                .TsName("emailAddress")
                .OpenApiFormat("email")
                .OpenApiDescription("Verified contact email.")
            .Property(c => c.Name)
                .OpenApiDescription("Display name shown in receipts.");
    }
}
public class Root { public object? El { get; set; } }
public class Dupa {
    public string Title { get; set; } = "";
    public Detail Info { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
}
public class Detail { public int Score { get; set; } }
public class Tag { public string Name { get; set; } = ""; }
