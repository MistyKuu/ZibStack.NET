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
    private const string DtoOnlyAttributeFqn = "ZibStack.NET.Dto.DtoOnlyAttribute";
    private const string DtoNameAttributeFqn = "ZibStack.NET.Dto.DtoNameAttribute";
    private const string FlattenAttributeFqn = "ZibStack.NET.Dto.FlattenAttribute";
    private const string QueryDtoAttributeFqn = "ZibStack.NET.Dto.QueryDtoAttribute";
    private const string ResponseDtoAttributeFqn = "ZibStack.NET.Dto.ResponseDtoAttribute";
    private const string UiImTiredOfCrudAttributeFqn = "ZibStack.NET.UI.ImTiredOfCrudAttribute";
    private const string CrudApiAttributeFqn = "ZibStack.NET.Dto.CrudApiAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Attributes + IDtoValidator in PostInit
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource("DtoTarget.g.cs", DtoTargetSource);
            ctx.AddSource("CreateDtoAttribute.g.cs", CreateDtoAttributeSource);
            ctx.AddSource("UpdateDtoAttribute.g.cs", UpdateDtoAttributeSource);
            ctx.AddSource("CreateOrUpdateDtoAttribute.g.cs", CreateOrUpdateDtoAttributeSource);
            ctx.AddSource("DtoIgnoreAttribute.g.cs", DtoIgnoreAttributeSource);
            ctx.AddSource("DtoOnlyAttribute.g.cs", DtoOnlyAttributeSource);
            ctx.AddSource("DtoNameAttribute.g.cs", DtoNameAttributeSource);
            ctx.AddSource("FlattenAttribute.g.cs", FlattenAttributeSource);
            ctx.AddSource("ResponseDtoAttribute.g.cs", ResponseDtoAttributeSource);
            ctx.AddSource("QueryDtoAttribute.g.cs", QueryDtoAttributeSource);
            ctx.AddSource("PaginatedResponse.g.cs", PaginatedResponseSource);
            ctx.AddSource("SortDirection.g.cs", SortDirectionSource);
            ctx.AddSource("IDtoValidator.g.cs", DtoValidatorInterfaceSource);
            ctx.AddSource("CreateDtoForAttribute.g.cs", CreateDtoForAttributeSource);
            ctx.AddSource("UpdateDtoForAttribute.g.cs", UpdateDtoForAttributeSource);
            ctx.AddSource("CrudOperations.g.cs", CrudOperationsSource);
            ctx.AddSource("ApiStyle.g.cs", ApiStyleSource);
            ctx.AddSource("CrudApiAttribute.g.cs", CrudApiAttributeSource);
            ctx.AddSource("ICrudStore.g.cs", CrudStoreInterfaceSource);
            ctx.AddSource("IDtoConfigurator.g.cs", ConfiguratorSource);
            ctx.AddSource("GenerateCrudTestsAttribute.g.cs", GenerateCrudTestsAttributeSource);
            ctx.AddSource("SignalRHubAttribute.g.cs", SignalRHubAttributeSource);
            ctx.AddSource("ETag.g.cs", ETagSource);
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

        // Parse fluent IDtoConfigurator once and feed it into every downstream pipeline.
        // CrudImplied / CrudApi / allDtoClasses callbacks combine with this so per-property
        // overrides + CrudApi options can mix with attribute markers in either direction.
        var fluentConfig = context.CompilationProvider.Select(static (compilation, _) =>
            DtoConfiguratorParser.Parse(compilation, _ => { }));

        // Detect ZibStack.NET.Query for DSL filter/sort/select support
        var hasQueryDsl = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("ZibStack.NET.Query.FilterParser") is not null);

        // Detect ZibStack.NET.TypeGen so we can stamp [GenerateTypes] on auto-generated
        // Create/Update/Response DTOs. This lets TypeGen's OpenAPI emitter discover them
        // naturally — without it, the $ref from [CrudApi] paths dangles because
        // TypeGen can't see other generators' output within one compilation pass.
        var hasTypeGen = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("ZibStack.NET.TypeGen.GenerateTypesAttribute") is not null);

        // Detect EF Core for Include generation in select
        var hasEfCore = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions") is not null);

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

        context.RegisterSourceOutput(allDtoClasses.Combine(hasTypeGen).Combine(fluentConfig), static (spc, pair) =>
        {
            var ((((createInfos, updateInfos), combinedInfos), hasTg), fluent) = pair;
            var nestedSeen = new HashSet<string>();

            foreach (var classInfo in createInfos)
            {
                classInfo.HasTypeGen = hasTg;
                ApplyFluentOverridesByClassName(classInfo, DtoKind.Create, fluent);
                var source = GenerateSource(classInfo);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Create.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen);
            }
            foreach (var classInfo in updateInfos)
            {
                classInfo.HasTypeGen = hasTg;
                ApplyFluentOverridesByClassName(classInfo, DtoKind.Update, fluent);
                var source = GenerateSource(classInfo);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Update.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen);
            }
            foreach (var classInfo in combinedInfos)
            {
                classInfo.HasTypeGen = hasTg;
                ApplyFluentOverridesByClassName(classInfo, DtoKind.Combined, fluent);
                var source = GenerateSource(classInfo);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Combined.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen);
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

        context.RegisterSourceOutput(responseDtoClasses.Combine(hasTypeGen), static (spc, pair) =>
        {
            var (info, hasTg) = pair;
            info.HasTypeGen = hasTg;
            var source = GenerateResponseDtoSource(info);
            spc.AddSource($"{info.FullyQualifiedName}.Response.g.cs", source);
            if (info.ListResponseName is not null && info.ListProperties is not null)
            {
                var listInfo = new ResponseDtoInfo(info.ClassName, info.Namespace, info.FullyQualifiedName,
                    info.ListResponseName, info.ListProperties) { HasTypeGen = hasTg };
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

        // [CrudApi] auto-implies missing DTO attributes — generate DTOs for classes
        // that have [CrudApi] but lack explicit [CreateDto]/[UpdateDto]/[ResponseDto]
        var crudImpliedDtos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrudApiAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => GetCrudImpliedDtos(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(crudImpliedDtos.Combine(hasQueryDsl).Combine(hasEfCore).Combine(hasTypeGen).Combine(fluentConfig), static (spc, pair) =>
        {
            var ((((implied, hasDsl), hasEf), hasTg), fluent) = pair;
            var nestedSeen = new HashSet<string>();

            foreach (var classInfo in implied.CreateDtos)
            {
                classInfo.HasTypeGen = hasTg;
                ApplyFluentOverridesByClassName(classInfo, DtoKind.Create, fluent);
                var source = GenerateSource(classInfo);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Create.CrudImplied.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen);
            }
            foreach (var classInfo in implied.UpdateDtos)
            {
                classInfo.HasTypeGen = hasTg;
                ApplyFluentOverridesByClassName(classInfo, DtoKind.Update, fluent);
                var source = GenerateSource(classInfo);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Update.CrudImplied.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen);
            }
            foreach (var responseInfo in implied.ResponseDtos)
            {
                responseInfo.HasTypeGen = hasTg;
                ApplyFluentResponseOverrides(responseInfo, fluent);
                var source = GenerateResponseDtoSource(responseInfo);
                spc.AddSource($"{responseInfo.FullyQualifiedName}.Response.CrudImplied.g.cs", source);
                if (responseInfo.ListResponseName is not null && responseInfo.ListProperties is not null)
                {
                    var listInfo = new ResponseDtoInfo(responseInfo.ClassName, responseInfo.Namespace, responseInfo.FullyQualifiedName,
                        responseInfo.ListResponseName, responseInfo.ListProperties) { HasTypeGen = hasTg };
                    var listSource = GenerateResponseDtoSource(listInfo);
                    spc.AddSource($"{responseInfo.FullyQualifiedName}.ListItem.CrudImplied.g.cs", listSource);
                }
            }
            foreach (var queryInfo in implied.QueryDtos)
            {
                queryInfo.HasQueryDsl = hasDsl;
                queryInfo.HasEfCore = hasEf;
                ApplyFluentQueryOverrides(queryInfo, fluent);
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

        context.RegisterSourceOutput(modelImpliedDtos.Combine(hasQueryDsl).Combine(hasEfCore).Combine(hasTypeGen).Combine(fluentConfig), static (spc, pair) =>
        {
            var ((((implied, hasDsl), hasEf), hasTg), fluent) = pair;
            var nestedSeen = new HashSet<string>();
            foreach (var classInfo in implied.CreateDtos)
            {
                classInfo.HasTypeGen = hasTg;
                ApplyFluentOverridesByClassName(classInfo, DtoKind.Create, fluent);
                var source = GenerateSource(classInfo);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Create.Model.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen);
            }
            foreach (var classInfo in implied.UpdateDtos)
            {
                classInfo.HasTypeGen = hasTg;
                ApplyFluentOverridesByClassName(classInfo, DtoKind.Update, fluent);
                var source = GenerateSource(classInfo);
                spc.AddSource($"{classInfo.FullyQualifiedName}.Update.Model.g.cs", source);
                EmitDeduplicatedNested(spc, classInfo.AutoNestedDtos, nestedSeen);
            }
            foreach (var responseInfo in implied.ResponseDtos)
            {
                responseInfo.HasTypeGen = hasTg;
                ApplyFluentResponseOverrides(responseInfo, fluent);
                var source = GenerateResponseDtoSource(responseInfo);
                spc.AddSource($"{responseInfo.FullyQualifiedName}.Response.Model.g.cs", source);
                if (responseInfo.ListResponseName is not null && responseInfo.ListProperties is not null)
                {
                    var listInfo = new ResponseDtoInfo(responseInfo.ClassName, responseInfo.Namespace, responseInfo.FullyQualifiedName,
                        responseInfo.ListResponseName, responseInfo.ListProperties) { HasTypeGen = hasTg };
                    var listSource = GenerateResponseDtoSource(listInfo);
                    spc.AddSource($"{responseInfo.FullyQualifiedName}.ListItem.Model.g.cs", listSource);
                }
            }
            foreach (var queryInfo in implied.QueryDtos)
            {
                queryInfo.HasQueryDsl = hasDsl;
                queryInfo.HasEfCore = hasEf;
                ApplyFluentQueryOverrides(queryInfo, fluent);
                var source = GenerateQueryDtoSource(queryInfo);
                spc.AddSource($"{queryInfo.FullyQualifiedName}.Query.Model.g.cs", source);
            }
        });

        // ── Fluent IDtoConfigurator pipeline ──────────────────────────────────
        // For classes with NO Dto attribute marker (CreateDto/UpdateDto/CrudApi/etc.),
        // fluent is the sole source of truth. For classes with markers, this pipeline
        // is a no-op — those go through the attribute pipelines above (which themselves
        // pick up fluent overrides via ApplyFluentOverridesByClassName).
        context.RegisterSourceOutput(fluentConfig.Combine(hasQueryDsl).Combine(hasEfCore).Combine(hasTypeGen),
            static (spc, pair) =>
        {
            var (((parsed, hasDsl), hasEf), hasTg) = pair;
            if (parsed is null) return;

            var nestedSeen = new HashSet<string>();
            foreach (var tc in parsed.ByType.Values)
            {
                if (tc.Symbol is null) continue;

                if (tc.Create)
                {
                    var info = BuildDtoClassInfoFromFluent(tc.Symbol, DtoKind.Create, tc, nestedSeen);
                    if (info is not null)
                    {
                        info.HasTypeGen = hasTg;
                        spc.AddSource($"{info.FullyQualifiedName}.Create.Fluent.g.cs", GenerateSource(info));
                        EmitDeduplicatedNested(spc, info.AutoNestedDtos, nestedSeen);
                    }
                }
                if (tc.Update)
                {
                    var info = BuildDtoClassInfoFromFluent(tc.Symbol, DtoKind.Update, tc, nestedSeen);
                    if (info is not null)
                    {
                        info.HasTypeGen = hasTg;
                        spc.AddSource($"{info.FullyQualifiedName}.Update.Fluent.g.cs", GenerateSource(info));
                        EmitDeduplicatedNested(spc, info.AutoNestedDtos, nestedSeen);
                    }
                }
                if (tc.CreateOrUpdate)
                {
                    var info = BuildDtoClassInfoFromFluent(tc.Symbol, DtoKind.Combined, tc, nestedSeen);
                    if (info is not null)
                    {
                        info.HasTypeGen = hasTg;
                        spc.AddSource($"{info.FullyQualifiedName}.Combined.Fluent.g.cs", GenerateSource(info));
                        EmitDeduplicatedNested(spc, info.AutoNestedDtos, nestedSeen);
                    }
                }
                if (tc.Response)
                {
                    var info = BuildResponseDtoInfoFromFluent(tc.Symbol, tc);
                    if (info is not null)
                    {
                        info.HasTypeGen = hasTg;
                        ApplyFluentResponseOverrides(info, parsed);
                        spc.AddSource($"{info.FullyQualifiedName}.Response.Fluent.g.cs", GenerateResponseDtoSource(info));
                        if (info.ListResponseName is not null && info.ListProperties is not null)
                        {
                            var listInfo = new ResponseDtoInfo(info.ClassName, info.Namespace, info.FullyQualifiedName,
                                info.ListResponseName, info.ListProperties) { HasTypeGen = hasTg };
                            spc.AddSource($"{info.FullyQualifiedName}.ListItem.Fluent.g.cs", GenerateResponseDtoSource(listInfo));
                        }
                    }
                }
                if (tc.Query)
                {
                    var info = BuildQueryDtoInfoFromFluent(tc.Symbol, tc);
                    if (info is not null)
                    {
                        info.HasQueryDsl = hasDsl;
                        info.HasEfCore = hasEf;
                        ApplyFluentQueryOverrides(info, parsed);
                        spc.AddSource($"{info.FullyQualifiedName}.Query.Fluent.g.cs", GenerateQueryDtoSource(info));
                    }
                }
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

        context.RegisterSourceOutput(crudApiDeclarations.Combine(hasAspNetCore).Combine(hasQueryDsl).Combine(fluentConfig), static (spc, pair) =>
        {
            var (((info, hasAsp), hasDsl), fluent) = pair;
            if (!hasAsp) return;
            info.HasQueryDsl = hasDsl;
            ApplyFluentCrudApiOverrides(info, fluent);

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

            // [ColumnPermission] masking helper shared by endpoints and controller
            if (info.ColumnPermissions.Count > 0 && info.HasResponseDto && info.ResponseName is not null)
                spc.AddSource($"{info.FullyQualifiedName}.ColumnPermissions.g.cs", GenerateColumnPermissionsSource(info));

            // Soft delete: emit IsDeleted + DeletedAt properties on the entity partial
            if (info.SoftDelete)
                spc.AddSource($"{info.FullyQualifiedName}.SoftDelete.g.cs", GenerateSoftDeleteProperties(info));

            // Concurrency: emit RowVersion property on the entity partial
            if (info.Concurrency && !info.HasUserRowVersion)
                spc.AddSource($"{info.FullyQualifiedName}.Concurrency.g.cs", GenerateConcurrencyProperties(info));

            // Audit: emit missing CreatedAt/UpdatedAt/CreatedBy/UpdatedBy on the entity partial
            if (info.Audit && info.AuditFieldsToGenerate.Count > 0)
                spc.AddSource($"{info.FullyQualifiedName}.Audit.g.cs", GenerateAuditProperties(info));

            // SignalR hub: emit hub class + client interface when [SignalRHub] is on the entity
            if (info.SignalR)
                spc.AddSource($"{info.FullyQualifiedName}.Hub.g.cs", GenerateSignalRHub(info));
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
            if (info.ColumnPermissions.Count > 0 && info.HasResponseDto && info.ResponseName is not null)
                spc.AddSource($"{info.FullyQualifiedName}.ColumnPermissions.Model.g.cs", GenerateColumnPermissionsSource(info));
            if (info.Concurrency && !info.HasUserRowVersion)
                spc.AddSource($"{info.FullyQualifiedName}.Concurrency.Model.g.cs", GenerateConcurrencyProperties(info));
            if (info.Audit && info.AuditFieldsToGenerate.Count > 0)
                spc.AddSource($"{info.FullyQualifiedName}.Audit.Model.g.cs", GenerateAuditProperties(info));
        });

        // ── [assembly: GenerateCrudTests] → xUnit integration test stubs ────
        var generateTests = context.CompilationProvider.Select(static (compilation, _) =>
        {
            bool hasMarker = compilation.Assembly.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Dto.GenerateCrudTestsAttribute");
            if (!hasMarker) return System.Array.Empty<CrudTestInfo>();

            // Detect ZibStack.NET.Query in the compilation (means endpoints have filter/sort/select)
            bool hasQueryDsl = false;
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol refAsm
                    && refAsm.Name == "ZibStack.NET.Query")
                { hasQueryDsl = true; break; }
            }

            var results = new List<CrudTestInfo>();
            // Scan own assembly + all referenced assemblies for [CrudApi] types
            ScanForCrudApi(compilation.Assembly.GlobalNamespace, results);
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol asm)
                    ScanForCrudApi(asm.GlobalNamespace, results);
            }

            if (hasQueryDsl)
                foreach (var r in results) r.HasQueryDsl = true;

            return results.ToArray();
        });

        context.RegisterSourceOutput(generateTests, static (spc, entities) =>
        {
            foreach (var entity in entities)
            {
                var hintName = $"{entity.Namespace?.Replace(".", "_")}_{entity.ClassName}".TrimStart('_');
                spc.AddSource($"{hintName}.CrudTests.g.cs", GenerateCrudTestSource(entity));
            }
        });
    }

    private static void EmitDeduplicatedNested(SourceProductionContext spc, List<DtoClassInfo> nested, HashSet<string> seen)
    {
        foreach (var n in nested)
        {
            var key = $"{n.RequestName}:{n.Kind}";
            if (!seen.Add(key)) continue;
            var source = GenerateSource(n);
            var hintName = n.FullyQualifiedName.Replace("?", "_").Replace("<", "_").Replace(">", "_");
            spc.AddSource($"{hintName}.{n.Kind}.AutoNested.g.cs", source);
            // Recurse for deeply nested
            EmitDeduplicatedNested(spc, n.AutoNestedDtos, seen);
        }
    }

}
