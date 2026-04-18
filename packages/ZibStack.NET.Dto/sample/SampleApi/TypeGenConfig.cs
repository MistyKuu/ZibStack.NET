using ZibStack.NET.Dto.Sample.Models;
using ZibStack.NET.TypeGen;

namespace ZibStack.NET.Dto.Sample;

/// <summary>
/// Fluent TypeGen config for the Dto sample. Shows the mapping knobs that are
/// awkward to express as attributes on the model (and which let you configure a
/// DTO without touching its source — same pattern you'd use for a type from a
/// referenced library).
/// </summary>
public sealed class TypeGenConfig : ITypeGenConfigurator
{
    public void Configure(ITypeGenBuilder b)
    {
        b.OpenApi(oa =>
        {
            oa.Title = "ZibStack Sample API";
            oa.Version = "1.0.0";
            oa.Description = "Full CRUD + schema + TS client contract, one attribute pass.";
        });

        
        b.TypeScript(ts => ts.OutputDir = "generated");
        // Article: declared so the parser recognizes the symbol — no .WithGeneratedTypes
        // here means the entity itself is NOT emitted as TS / OpenAPI. It's only here to
        // anchor companion lookups below. (You CAN still chain config like .TsName(...)
        // / .Property(...) which would apply IF the type ever does get emitted.)
  
        // Explicit per-companion opt-in. Synthesizes ONLY the Create variant from
        // Article's properties — UpdateArticleRequest / ArticleResponse are NOT emitted
        // because they aren't listed.
        b.ForType<CreateArticleRequest>()
            .WithGeneratedTypes(TypeTarget.TypeScript | TypeTarget.OpenApi)
            .TsName("ArticleDtao");

        // Player: demonstrate per-property mapping overrides.
        b.ForType<Player>()
            .WithGeneratedTypes(TypeTarget.Python)
            // decimal → number (OpenAPI default) loses precision on the wire; force
            // string so JSON parsers don't round-trip through float64.
            .Property(x => x.Salary)
                .OpenApiType("string")
                .OpenApiFormat("decimal")
                .OpenApiDescription("Encoded as string to preserve precision.")
            // Password is already [DtoIgnore(DtoTarget.Response)] at runtime but the
            // response DTO is auto-generated — belt-and-suspenders: also hide it from
            // the OpenAPI and TS contracts explicitly.
            .Property(x => x.Password).Ignore()
            // Email format hint — attribute could do it, DSL version shown for parity.
            .Property(x => x.Email)
                .OpenApiFormat("email")
                .OpenApiDescription("Contact email (optional).")
            // Bio is a long text column; give frontend a wider TS type hint.
            .Property(x => x.Bio)
                .TsType("string | null")
                .OpenApiFormat("textarea");

        // Team: one small rename to show Response-DTO style naming without editing
        // the model. Matches the $ref the Dto-generated response DTO would use.
        b.ForType<Team>()
            .OpenApiName("TeamResponse");
    }
}
