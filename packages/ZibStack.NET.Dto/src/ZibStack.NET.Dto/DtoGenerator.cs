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
    private const string DtoIgnoreAttributeFqn = "ZibStack.NET.Dto.DtoIgnoreAttribute";
    private const string DtoNameAttributeFqn = "ZibStack.NET.Dto.DtoNameAttribute";
    private const string CreateOnlyAttributeFqn = "ZibStack.NET.Dto.CreateOnlyAttribute";
    private const string UpdateOnlyAttributeFqn = "ZibStack.NET.Dto.UpdateOnlyAttribute";
    private const string ImmutableAttributeFqn = "ZibStack.NET.Dto.ImmutableAttribute";
    private const string FlattenAttributeFqn = "ZibStack.NET.Dto.FlattenAttribute";
    private const string RenamePropertyAttributeFqn = "ZibStack.NET.Dto.RenamePropertyAttribute";
    private const string QueryDtoAttributeFqn = "ZibStack.NET.Dto.QueryDtoAttribute";
    private const string ZQueryAttributeFqn = "ZibStack.NET.Dto.ZQueryAttribute";
    private const string ResponseDtoAttributeFqn = "ZibStack.NET.Dto.ResponseDtoAttribute";
    private const string ResponseIgnoreAttributeFqn = "ZibStack.NET.Dto.ResponseIgnoreAttribute";
    private const string QueryIgnoreAttributeFqn = "ZibStack.NET.Dto.QueryIgnoreAttribute";
    private const string ListIgnoreAttributeFqn = "ZibStack.NET.Dto.ListIgnoreAttribute";
    private const string UiImTiredOfCrudAttributeFqn = "ZibStack.NET.UI.ImTiredOfCrudAttribute";
    private const string CrudApiAttributeFqn = "ZibStack.NET.Dto.CrudApiAttribute";

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
            ctx.AddSource("QueryDtoAttribute.g.cs", QueryDtoAttributeSource);
            ctx.AddSource("ZQueryAttribute.g.cs", ZQueryAttributeSource);
            ctx.AddSource("ResponseIgnoreAttribute.g.cs", ResponseIgnoreAttributeSource);
            ctx.AddSource("PaginatedResponse.g.cs", PaginatedResponseSource);
            ctx.AddSource("SortDirection.g.cs", SortDirectionSource);
            ctx.AddSource("IDtoValidator.g.cs", DtoValidatorInterfaceSource);
            ctx.AddSource("CreateDtoForAttribute.g.cs", CreateDtoForAttributeSource);
            ctx.AddSource("UpdateDtoForAttribute.g.cs", UpdateDtoForAttributeSource);
            ctx.AddSource("CrudOperations.g.cs", CrudOperationsSource);
            ctx.AddSource("ApiStyle.g.cs", ApiStyleSource);
            ctx.AddSource("CrudApiAttribute.g.cs", CrudApiAttributeSource);
            ctx.AddSource("QueryIgnoreAttribute.g.cs", QueryIgnoreAttributeSource);
            ctx.AddSource("ListIgnoreAttribute.g.cs", ListIgnoreAttributeSource);
            ctx.AddSource("ICrudStore.g.cs", CrudStoreInterfaceSource);
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

        // Detect Swashbuckle and emit schema filter (v10+ uses IOpenApiSchema, older uses OpenApiSchema)
        var swashbuckleVersion = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var hasFilter = compilation.GetTypeByMetadataName("Swashbuckle.AspNetCore.SwaggerGen.ISchemaFilter") is not null;
            if (!hasFilter) return 0;
            var hasNewApi = compilation.GetTypeByMetadataName("Microsoft.OpenApi.IOpenApiSchema") is not null;
            return hasNewApi ? 2 : 1; // 0=none, 1=legacy, 2=v10+
        });

        context.RegisterSourceOutput(swashbuckleVersion, static (spc, version) =>
        {
            if (version == 1)
                spc.AddSource("PatchFieldSchemaFilter.g.cs", PatchFieldSchemaFilterSource);
            else if (version == 2)
                spc.AddSource("PatchFieldSchemaFilter.g.cs", PatchFieldSchemaFilterV10Source);
        });

        // Detect FluentValidation and emit adapter
        var hasFluentValidation = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("FluentValidation.AbstractValidator`1") is not null);

        // Detect ZibStack.NET.Query for DSL filter/sort/select support
        var hasQueryDsl = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("ZibStack.NET.Query.FilterParser") is not null);

        // Detect EF Core for Include generation in select
        var hasEfCore = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions") is not null);

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

        // SDTO012: [CrudApi] type with no matching DbSet<T> in any DbContext.
        // Without a registered ICrudStore<T,TKey>, generated endpoints fail at startup
        // with an obscure ASP.NET body-inference error. Catch it at compile time.
        var crudApiDiagTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrudApiAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetCrudApiDiagTarget(ctx))
            .Where(static t => t is not null)
            .Select(static (t, _) => t!);

        // Also pick up [ImTiredOfCrud] (from ZibStack.NET.UI) which generates the
        // same endpoints and has the same store dependency.
        var modelCrudApiDiagTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UiImTiredOfCrudAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var hasCrud = ((INamedTypeSymbol)ctx.TargetSymbol).GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == CrudApiAttributeFqn);
                    return hasCrud ? null : GetCrudApiDiagTarget(ctx);
                })
            .Where(static t => t is not null)
            .Select(static (t, _) => t!);

        var allCrudApiDiagTargets = crudApiDiagTargets.Collect()
            .Combine(modelCrudApiDiagTargets.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        // Walk the whole compilation for DbContext-derived classes with DbSet<T> properties.
        // CompilationProvider re-runs on every compilation snapshot, so this picks up
        // brand-new DbSet declarations the user just added without stale cache.
        var dbSetEntityTypes = context.CompilationProvider.Select(static (compilation, ct) =>
        {
            var set = new HashSet<string>();
            var dbContextSymbol = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            if (dbContextSymbol is null) return set;

            void Visit(INamespaceSymbol ns)
            {
                foreach (var type in ns.GetTypeMembers())
                    VisitType(type);
                foreach (var nested in ns.GetNamespaceMembers())
                    Visit(nested);
            }

            void VisitType(INamedTypeSymbol type)
            {
                ct.ThrowIfCancellationRequested();

                // Walk base chain — does it derive from DbContext?
                bool isDbContext = false;
                var bt = type.BaseType;
                while (bt is not null)
                {
                    if (SymbolEqualityComparer.Default.Equals(bt, dbContextSymbol)) { isDbContext = true; break; }
                    bt = bt.BaseType;
                }

                if (isDbContext)
                {
                    foreach (var member in type.GetMembers())
                    {
                        if (member is not IPropertySymbol prop) continue;
                        if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                        if (prop.Type is not INamedTypeSymbol propType || !propType.IsGenericType) continue;
                        if (propType.ConstructedFrom.ToDisplayString() != "Microsoft.EntityFrameworkCore.DbSet<TEntity>") continue;
                        if (propType.TypeArguments[0] is INamedTypeSymbol entity)
                            set.Add(entity.ToDisplayString());
                    }
                }

                foreach (var nested in type.GetTypeMembers())
                    VisitType(nested);
            }

            Visit(compilation.Assembly.GlobalNamespace);
            return set;
        });

        context.RegisterSourceOutput(allCrudApiDiagTargets.Combine(dbSetEntityTypes), static (spc, pair) =>
        {
            var (targets, dbSetTypes) = pair;
            foreach (var target in targets)
            {
                if (dbSetTypes.Contains(target.FullyQualifiedName)) continue;
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.CrudApiMissingStore,
                    target.Location?.ToLocation() ?? Location.None,
                    target.ClassName,
                    target.KeyTypeName));
            }
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
            if (info.ListResponseName is not null && info.ListProperties is not null)
            {
                var listInfo = new ResponseDtoInfo(info.ClassName, info.Namespace, info.FullyQualifiedName,
                    info.ListResponseName, info.ListProperties);
                var listSource = GenerateResponseDtoSource(listInfo);
                spc.AddSource($"{info.FullyQualifiedName}.ListItem.g.cs", listSource);
            }
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

        // Find [QueryDto] classes
        var queryDtoDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                QueryDtoAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetQueryDtoInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(queryDtoDeclarations.Combine(hasQueryDsl).Combine(hasEfCore), static (spc, pair) =>
        {
            var ((info, hasDsl), hasEf) = pair;
            info.HasQueryDsl = hasDsl;
            info.HasEfCore = hasEf;
            var source = GenerateQueryDtoSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Query.g.cs", source);
        });

        // Find [ZQuery] classes (standalone alias for [QueryDto])
        var zQueryDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ZQueryAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetQueryDtoInfo(ctx, ZQueryAttributeFqn))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(zQueryDeclarations.Combine(hasQueryDsl).Combine(hasEfCore), static (spc, pair) =>
        {
            var ((info, hasDsl), hasEf) = pair;
            info.HasQueryDsl = hasDsl;
            info.HasEfCore = hasEf;
            var source = GenerateQueryDtoSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.ZQuery.g.cs", source);
        });

        // [CrudApi] auto-implies missing DTO attributes — generate DTOs for classes
        // that have [CrudApi] but lack explicit [CreateDto]/[UpdateDto]/[ResponseDto]
        var crudImpliedDtos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrudApiAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetCrudImpliedDtos(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(crudImpliedDtos.Combine(hasFluentValidation).Combine(hasQueryDsl).Combine(hasEfCore), static (spc, pair) =>
        {
            var (((implied, hasFluent), hasDsl), hasEf) = pair;
            var nestedSeen = new HashSet<string>();

            foreach (var classInfo in implied.CreateDtos)
            {
                var source = GenerateSource(classInfo, hasFluent);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Create.CrudImplied.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen, hasFluent);
            }
            foreach (var classInfo in implied.UpdateDtos)
            {
                var source = GenerateSource(classInfo, hasFluent);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Update.CrudImplied.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen, hasFluent);
            }
            foreach (var responseInfo in implied.ResponseDtos)
            {
                var source = GenerateResponseDtoSource(responseInfo);
                spc.AddSource($"{responseInfo.FullyQualifiedName}.Response.CrudImplied.g.cs", source);
                if (responseInfo.ListResponseName is not null && responseInfo.ListProperties is not null)
                {
                    var listInfo = new ResponseDtoInfo(responseInfo.ClassName, responseInfo.Namespace, responseInfo.FullyQualifiedName,
                        responseInfo.ListResponseName, responseInfo.ListProperties);
                    var listSource = GenerateResponseDtoSource(listInfo);
                    spc.AddSource($"{responseInfo.FullyQualifiedName}.ListItem.CrudImplied.g.cs", listSource);
                }
            }
            foreach (var queryInfo in implied.QueryDtos)
            {
                queryInfo.HasQueryDsl = hasDsl;
                queryInfo.HasEfCore = hasEf;
                var source = GenerateQueryDtoSource(queryInfo);
                spc.AddSource($"{queryInfo.FullyQualifiedName}.Query.CrudImplied.g.cs", source);
            }
        });

        // [ImTiredOfCrud] (from ZibStack.NET.UI) → same as [CrudApi] — implied DTOs
        var modelImpliedDtos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UiImTiredOfCrudAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    // Skip if explicit [CrudApi] exists
                    var hasCrud = ((INamedTypeSymbol)ctx.TargetSymbol).GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Dto.CrudApiAttribute");
                    return hasCrud ? null : GetCrudImpliedDtos(ctx);
                })
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(modelImpliedDtos.Combine(hasFluentValidation).Combine(hasQueryDsl).Combine(hasEfCore), static (spc, pair) =>
        {
            var (((implied, hasFluent), hasDsl), hasEf) = pair;
            var nestedSeen = new HashSet<string>();
            foreach (var classInfo in implied.CreateDtos)
            {
                var source = GenerateSource(classInfo, hasFluent);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Create.Model.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen, hasFluent);
            }
            foreach (var classInfo in implied.UpdateDtos)
            {
                var source = GenerateSource(classInfo, hasFluent);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Update.Model.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen, hasFluent);
            }
            foreach (var responseInfo in implied.ResponseDtos)
            {
                var source = GenerateResponseDtoSource(responseInfo);
                spc.AddSource($"{responseInfo.FullyQualifiedName}.Response.Model.g.cs", source);
                if (responseInfo.ListResponseName is not null && responseInfo.ListProperties is not null)
                {
                    var listInfo = new ResponseDtoInfo(responseInfo.ClassName, responseInfo.Namespace, responseInfo.FullyQualifiedName,
                        responseInfo.ListResponseName, responseInfo.ListProperties);
                    var listSource = GenerateResponseDtoSource(listInfo);
                    spc.AddSource($"{responseInfo.FullyQualifiedName}.ListItem.Model.g.cs", listSource);
                }
            }
            foreach (var queryInfo in implied.QueryDtos)
            {
                queryInfo.HasQueryDsl = hasDsl;
                queryInfo.HasEfCore = hasEf;
                var source = GenerateQueryDtoSource(queryInfo);
                spc.AddSource($"{queryInfo.FullyQualifiedName}.Query.Model.g.cs", source);
            }
        });

        // Detect ASP.NET Core for CRUD API generation
        var hasAspNetCore = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Results") is not null);

        // Find [CrudApi] classes
        var crudApiDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrudApiAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetCrudApiInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(crudApiDeclarations.Combine(hasAspNetCore).Combine(hasQueryDsl), static (spc, pair) =>
        {
            var ((info, hasAsp), hasDsl) = pair;
            if (!hasAsp) return;
            info.HasQueryDsl = hasDsl;

            // ApiStyle: 0=MinimalApi, 1=Controller, 2=Both
            if (info.Style == StyleMinimalApi || info.Style == StyleBoth)
            {
                var source = GenerateMinimalApiSource(info);
                spc.AddSource($"{info.FullyQualifiedName}.Endpoints.g.cs", source);
            }

            if (info.Style == StyleController || info.Style == StyleBoth)
            {
                var source = GenerateControllerSource(info);
                spc.AddSource($"{info.FullyQualifiedName}.Controller.g.cs", source);
            }

            // Emit code map partial class with summary linking to all generated types
            spc.AddSource($"{info.FullyQualifiedName}.CodeMap.g.cs", GenerateCodeMap(info));
        });

        // [ImTiredOfCrud] (from ZibStack.NET.UI) → CRUD endpoints
        var modelCrudDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UiImTiredOfCrudAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var hasCrud = ((INamedTypeSymbol)ctx.TargetSymbol).GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Dto.CrudApiAttribute");
                    return hasCrud ? null : GetCrudApiInfo(ctx);
                })
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(modelCrudDeclarations.Combine(hasAspNetCore).Combine(hasQueryDsl), static (spc, pair) =>
        {
            var ((info, hasAsp), hasDsl) = pair;
            if (!hasAsp) return;
            info.HasQueryDsl = hasDsl;
            if (info.Style == StyleMinimalApi || info.Style == StyleBoth)
                spc.AddSource($"{info.FullyQualifiedName}.Endpoints.Model.g.cs", GenerateMinimalApiSource(info));
            if (info.Style == StyleController || info.Style == StyleBoth)
                spc.AddSource($"{info.FullyQualifiedName}.Controller.Model.g.cs", GenerateControllerSource(info));
            spc.AddSource($"{info.FullyQualifiedName}.CodeMap.Model.g.cs", GenerateCodeMap(info));
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
