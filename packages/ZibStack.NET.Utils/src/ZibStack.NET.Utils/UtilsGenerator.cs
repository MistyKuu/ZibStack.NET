using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Utils;

[Generator]
public partial class UtilsGenerator : IIncrementalGenerator
{
    private const string PartialFromAttributeFqn = "ZibStack.NET.Utils.PartialFromAttribute";
    private const string IntersectFromAttributeFqn = "ZibStack.NET.Utils.IntersectFromAttribute";
    private const string PickFromAttributeFqn = "ZibStack.NET.Utils.PickFromAttribute";
    private const string OmitFromAttributeFqn = "ZibStack.NET.Utils.OmitFromAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Attributes in PostInit
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource("PartialFromAttribute.g.cs", PartialFromAttributeSource);
            ctx.AddSource("IntersectFromAttribute.g.cs", IntersectFromAttributeSource);
            ctx.AddSource("PickFromAttribute.g.cs", PickFromAttributeSource);
            ctx.AddSource("OmitFromAttribute.g.cs", OmitFromAttributeSource);
            ctx.AddSource("PaginatedResponse.g.cs", PaginatedResponseSource);
            ctx.AddSource("SortDirection.g.cs", SortDirectionSource);
        });

        // Detect available serializers and emit PatchField + converters
        var serializerFlags = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var hasStj = compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonConverter") is not null;
            var hasNewtonsoft = compilation.GetTypeByMetadataName("Newtonsoft.Json.JsonConverter") is not null;
            return (hasStj, hasNewtonsoft);
        });

        context.RegisterSourceOutput(serializerFlags, static (spc, flags) =>
        {
            spc.AddSource("PatchField.g.cs", GeneratePatchFieldSource(flags.hasStj, flags.hasNewtonsoft));
        });

        // Detect Swashbuckle and emit schema filter
        var hasSwashbuckle = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Swashbuckle.AspNetCore.SwaggerGen.ISchemaFilter") is not null);

        context.RegisterSourceOutput(hasSwashbuckle, static (spc, has) =>
        {
            if (has)
                spc.AddSource("PatchFieldSchemaFilter.g.cs", PatchFieldSchemaFilterSource);
        });

        // Find [PartialFrom(typeof(...))] classes
        var partialFromDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PartialFromAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetPartialFromInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(partialFromDeclarations, static (spc, info) =>
        {
            var source = GeneratePartialFromSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Partial.g.cs", source);
        });

        // Find [IntersectFrom(...)] classes — collect and deduplicate
        var intersectRaw = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                IntersectFromAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetIntersectFromInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(intersectRaw.Collect(), static (spc, infos) =>
        {
            var seen = new HashSet<string>();
            foreach (var info in infos)
            {
                if (seen.Add(info.FullyQualifiedName))
                {
                    var source = GenerateIntersectSource(info);
                    spc.AddSource($"{info.FullyQualifiedName}.Intersect.g.cs", source);
                }
            }
        });

        // Find [PickFrom] classes
        var pickFromDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PickFromAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetPickOmitInfo(ctx, PickFromAttributeFqn, isPick: true))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(pickFromDeclarations, static (spc, info) =>
        {
            var source = GeneratePartialFromSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Pick.g.cs", source);
        });

        // Find [OmitFrom] classes
        var omitFromDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OmitFromAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetPickOmitInfo(ctx, OmitFromAttributeFqn, isPick: false))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(omitFromDeclarations, static (spc, info) =>
        {
            var source = GeneratePartialFromSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Omit.g.cs", source);
        });
    }
}
