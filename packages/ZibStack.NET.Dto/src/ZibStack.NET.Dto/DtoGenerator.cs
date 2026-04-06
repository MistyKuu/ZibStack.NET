using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Dto;

[Generator]
public partial class DtoGenerator : IIncrementalGenerator
{
    private const string CreateDtoAttributeFqn = "ZibStack.NET.Dto.CreateDtoAttribute";
    private const string UpdateDtoAttributeFqn = "ZibStack.NET.Dto.UpdateDtoAttribute";
    private const string CreateOrUpdateDtoAttributeFqn = "ZibStack.NET.Dto.CreateOrUpdateDtoAttribute";
    private const string CreateDtoForAttributeFqn = "ZibStack.NET.Dto.CreateDtoForAttribute";
    private const string UpdateDtoForAttributeFqn = "ZibStack.NET.Dto.UpdateDtoForAttribute";
    private const string PartialFromAttributeFqn = "ZibStack.NET.Dto.PartialFromAttribute";
    private const string IntersectFromAttributeFqn = "ZibStack.NET.Dto.IntersectFromAttribute";
    private const string DtoIgnoreAttributeFqn = "ZibStack.NET.Dto.DtoIgnoreAttribute";
    private const string DtoNameAttributeFqn = "ZibStack.NET.Dto.DtoNameAttribute";
    private const string CreateOnlyAttributeFqn = "ZibStack.NET.Dto.CreateOnlyAttribute";
    private const string UpdateOnlyAttributeFqn = "ZibStack.NET.Dto.UpdateOnlyAttribute";
    private const string ImmutableAttributeFqn = "ZibStack.NET.Dto.ImmutableAttribute";
    private const string FlattenAttributeFqn = "ZibStack.NET.Dto.FlattenAttribute";
    private const string RenamePropertyAttributeFqn = "ZibStack.NET.Dto.RenamePropertyAttribute";
    private const string PickFromAttributeFqn = "ZibStack.NET.Dto.PickFromAttribute";
    private const string OmitFromAttributeFqn = "ZibStack.NET.Dto.OmitFromAttribute";
    private const string QueryDtoAttributeFqn = "ZibStack.NET.Dto.QueryDtoAttribute";
    private const string ResponseDtoAttributeFqn = "ZibStack.NET.Dto.ResponseDtoAttribute";
    private const string ResponseIgnoreAttributeFqn = "ZibStack.NET.Dto.ResponseIgnoreAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Attributes + IDtoValidator in PostInit
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource("CreateDtoAttribute.g.cs", CreateDtoAttributeSource);
            ctx.AddSource("UpdateDtoAttribute.g.cs", UpdateDtoAttributeSource);
            ctx.AddSource("CreateOrUpdateDtoAttribute.g.cs", CreateOrUpdateDtoAttributeSource);
            ctx.AddSource("DtoIgnoreAttribute.g.cs", DtoIgnoreAttributeSource);
            ctx.AddSource("DtoNameAttribute.g.cs", DtoNameAttributeSource);
            ctx.AddSource("CreateOnlyAttribute.g.cs", CreateOnlyAttributeSource);
            ctx.AddSource("UpdateOnlyAttribute.g.cs", UpdateOnlyAttributeSource);
            ctx.AddSource("ImmutableAttribute.g.cs", ImmutableAttributeSource);
            ctx.AddSource("FlattenAttribute.g.cs", FlattenAttributeSource);
            ctx.AddSource("RenamePropertyAttribute.g.cs", RenamePropertyAttributeSource);
            ctx.AddSource("ResponseDtoAttribute.g.cs", ResponseDtoAttributeSource);
            ctx.AddSource("PickFromAttribute.g.cs", PickFromAttributeSource);
            ctx.AddSource("OmitFromAttribute.g.cs", OmitFromAttributeSource);
            ctx.AddSource("QueryDtoAttribute.g.cs", QueryDtoAttributeSource);
            ctx.AddSource("ResponseIgnoreAttribute.g.cs", ResponseIgnoreAttributeSource);
            ctx.AddSource("PaginatedResponse.g.cs", PaginatedResponseSource);
            ctx.AddSource("SortDirection.g.cs", SortDirectionSource);
            ctx.AddSource("IDtoValidator.g.cs", DtoValidatorInterfaceSource);
            ctx.AddSource("CreateDtoForAttribute.g.cs", CreateDtoForAttributeSource);
            ctx.AddSource("UpdateDtoForAttribute.g.cs", UpdateDtoForAttributeSource);
            ctx.AddSource("PartialFromAttribute.g.cs", PartialFromAttributeSource);
            ctx.AddSource("IntersectFromAttribute.g.cs", IntersectFromAttributeSource);
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

        // Detect FluentValidation and emit adapter
        var hasFluentValidation = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("FluentValidation.AbstractValidator`1") is not null);

        context.RegisterSourceOutput(hasFluentValidation, static (spc, hasFluent) =>
        {
            if (hasFluent)
                spc.AddSource("FluentDtoValidator.g.cs", FluentDtoValidatorSource);
        });

        // Run diagnostics on all types with ZibStack.Dto attributes
        var diagnosticsTargets = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax tds &&
                    tds.AttributeLists.Count > 0,
                transform: static (ctx, _) =>
                {
                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol;
                    if (symbol is null) return ((INamedTypeSymbol?, TypeDeclarationSyntax?)) (null, null);
                    var hasDtoAttribute = symbol.GetAttributes().Any(a =>
                    {
                        var name = a.AttributeClass?.ContainingNamespace?.ToDisplayString();
                        return name == "ZibStack.NET.Dto";
                    });
                    if (!hasDtoAttribute) return (null, null);
                    return (symbol, (TypeDeclarationSyntax)ctx.Node);
                })
            .Where(static x => x.Item1 is not null);

        context.RegisterSourceOutput(diagnosticsTargets, static (spc, pair) =>
        {
            RunDiagnostics(spc, pair.Item1!, pair.Item2!);
        });

        // Find [CreateDto] classes
        var createDtoClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CreateDtoAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetDtoInfo(ctx, DtoKind.Create, CreateDtoAttributeFqn))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Find [UpdateDto] classes
        var updateDtoClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UpdateDtoAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetDtoInfo(ctx, DtoKind.Update, UpdateDtoAttributeFqn))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Find [CreateOrUpdateDto] classes
        var combinedDtoClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CreateOrUpdateDtoAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetDtoInfo(ctx, DtoKind.Combined, CreateOrUpdateDtoAttributeFqn))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Collect ALL Dto classes, emit main DTOs + globally deduplicated auto-nested
        var allDtoClasses = createDtoClasses.Collect()
            .Combine(updateDtoClasses.Collect())
            .Combine(combinedDtoClasses.Collect());

        context.RegisterSourceOutput(allDtoClasses.Combine(hasFluentValidation), static (spc, pair) =>
        {
            var (((createInfos, updateInfos), combinedInfos), hasFluent) = pair;
            var nestedSeen = new HashSet<string>();

            foreach (var classInfo in createInfos)
            {
                var source = GenerateSource(classInfo, hasFluent);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Create.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen, hasFluent);
            }
            foreach (var classInfo in updateInfos)
            {
                var source = GenerateSource(classInfo, hasFluent);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Update.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen, hasFluent);
            }
            foreach (var classInfo in combinedInfos)
            {
                var source = GenerateSource(classInfo, hasFluent);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Combined.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen, hasFluent);
            }
        });

        // Find [ResponseDto] classes
        var responseDtoClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ResponseDtoAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetResponseDtoInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(responseDtoClasses, static (spc, info) =>
        {
            var source = GenerateResponseDtoSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Response.g.cs", source);
        });

        // Find [CreateDtoFor] and [UpdateDtoFor] — generate partial records for external types
        var createDtoForDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CreateDtoForAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetCreateDtoForInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(createDtoForDeclarations, static (spc, info) =>
        {
            var source = GenerateCreateDtoForSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.CreateDtoFor.g.cs", source);
        });

        var updateDtoForDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UpdateDtoForAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetUpdateDtoForInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(updateDtoForDeclarations, static (spc, info) =>
        {
            var source = GenerateUpdateDtoForSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.UpdateDtoFor.g.cs", source);
        });

        // Find [PartialFrom(typeof(...))] classes and generate partial with PatchField props + ApplyTo
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

        // Find [QueryDto] classes
        var queryDtoDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                QueryDtoAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetQueryDtoInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(queryDtoDeclarations, static (spc, info) =>
        {
            var source = GenerateQueryDtoSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Query.g.cs", source);
        });
    }

    private static void EmitDeduplicatedNested(SourceProductionContext spc, List<DtoClassInfo> nested, HashSet<string> seen, bool hasFluent)
    {
        foreach (var n in nested)
        {
            var key = $"{n.RequestName}:{n.Kind}";
            if (!seen.Add(key)) continue;
            var source = GenerateSource(n, hasFluent);
            var hintName = n.FullyQualifiedName.Replace("?", "_").Replace("<", "_").Replace(">", "_");
            spc.AddSource($"{hintName}.{n.Kind}.AutoNested.g.cs", source);
            // Recurse for deeply nested
            EmitDeduplicatedNested(spc, n.AutoNestedDtos, seen, hasFluent);
        }
    }

}
