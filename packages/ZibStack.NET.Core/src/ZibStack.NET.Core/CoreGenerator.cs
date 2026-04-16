using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Core;

[Generator]
public partial class CoreGenerator : IIncrementalGenerator
{
    // ─── Utility type attributes ────────────────────────────────────────
    private const string PartialFromAttributeFqn = "ZibStack.NET.Core.PartialFromAttribute";
    private const string IntersectFromAttributeFqn = "ZibStack.NET.Core.IntersectFromAttribute";
    private const string PickFromAttributeFqn = "ZibStack.NET.Core.PickFromAttribute";
    private const string OmitFromAttributeFqn = "ZibStack.NET.Core.OmitFromAttribute";

    // ─── Relationship / entity attributes ───────────────────────────────
    private const string OneToManyAttributeFqn = "ZibStack.NET.Core.OneToManyAttribute";
    private const string OneToOneAttributeFqn = "ZibStack.NET.Core.OneToOneAttribute";
    private const string EntityAttributeFqn = "ZibStack.NET.Core.EntityAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            // Utility type attributes
            ctx.AddSource("PartialFromAttribute.g.cs", PartialFromAttributeSource);
            ctx.AddSource("IntersectFromAttribute.g.cs", IntersectFromAttributeSource);
            ctx.AddSource("PickFromAttribute.g.cs", PickFromAttributeSource);
            ctx.AddSource("OmitFromAttribute.g.cs", OmitFromAttributeSource);
            ctx.AddSource("DestructurableAttribute.g.cs", DestructurableAttributeSource);

            // Relationship / entity attributes
            ctx.AddSource("OneToManyAttribute.g.cs", OneToManyAttributeSource);
            ctx.AddSource("OneToOneAttribute.g.cs", OneToOneAttributeSource);
            ctx.AddSource("EntityAttribute.g.cs", EntityAttributeSource);
        });

        // [Destructurable<TSource>] — shape-record path: emits nested Rest + Split factory
        RegisterDestructurable(context);

        // [PartialFrom]
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

        // [IntersectFrom]
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
                    var source = GenerateIntersectPlainSource(info);
                    spc.AddSource($"{info.FullyQualifiedName}.Intersect.g.cs", source);
                }
            }
        });

        // [PickFrom]
        var pickFromDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PickFromAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetPickOmitInfo(ctx, PickFromAttributeFqn, isPick: true))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(pickFromDeclarations, static (spc, info) =>
        {
            var source = GeneratePlainShapeSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Pick.g.cs", source);
        });

        // [OmitFrom]
        var omitFromDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OmitFromAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetPickOmitInfo(ctx, OmitFromAttributeFqn, isPick: false))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(omitFromDeclarations, static (spc, info) =>
        {
            var source = GeneratePlainShapeSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Omit.g.cs", source);
        });
    }
}
