using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Validation;

[Generator]
public sealed partial class ValidationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource("ZValidateAttribute.g.cs", ZValidateAttributeSource);
            ctx.AddSource("ValidationResult.g.cs", ValidationResultSource);
            ctx.AddSource("ZRequiredAttribute.g.cs", ZRequiredAttributeSource);
            ctx.AddSource("ZMinLengthAttribute.g.cs", ZMinLengthAttributeSource);
            ctx.AddSource("ZMaxLengthAttribute.g.cs", ZMaxLengthAttributeSource);
            ctx.AddSource("ZRangeAttribute.g.cs", ZRangeAttributeSource);
            ctx.AddSource("ZEmailAttribute.g.cs", ZEmailAttributeSource);
            ctx.AddSource("ZUrlAttribute.g.cs", ZUrlAttributeSource);
            ctx.AddSource("ZMatchAttribute.g.cs", RegexAttributeSource);
            ctx.AddSource("ZNotEmptyAttribute.g.cs", ZNotEmptyAttributeSource);
            ctx.AddSource("ZInAttribute.g.cs", ZInAttributeSource);
            ctx.AddSource("ZNotInAttribute.g.cs", ZNotInAttributeSource);
            ctx.AddSource("ZCreditCardAttribute.g.cs", ZCreditCardAttributeSource);
            ctx.AddSource("ZPhoneAttribute.g.cs", ZPhoneAttributeSource);
            ctx.AddSource("ZCascadeAttribute.g.cs", ZCascadeAttributeSource);
            ctx.AddSource("IValidationConfigurator.g.cs", CrossFieldInterfacesSource);
        });

        // Emit the ASP.NET Core endpoint filter only when EVERY type the generated
        // source references is actually resolvable in this compilation. Previously the
        // gate only checked `Microsoft.AspNetCore.Http.Results`, so any project that
        // happened to transitively pull that assembly in (e.g. tests referencing an
        // ASP.NET Core web project for WebApplicationFactory, but not wiring the full
        // minimal-API extension surface) hit a compile error:
        //   `RouteHandlerBuilder does not contain a definition for AddEndpointFilter`.
        // The three types below live in three separate assemblies — RouteHandlerBuilder
        // in Microsoft.AspNetCore.Routing, Results in Microsoft.AspNetCore.Http.Results,
        // EndpointFilterExtensions (which supplies AddEndpointFilter) in
        // Microsoft.AspNetCore.Http.Extensions — so all three must be present before
        // the filter source can compile.
        context.RegisterSourceOutput(
            context.CompilationProvider.Select(static (comp, _) =>
                comp.GetTypeByMetadataName("Microsoft.AspNetCore.Http.EndpointFilterExtensions") is not null
                && comp.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Results") is not null
                && comp.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.RouteHandlerBuilder") is not null),
            static (spc, hasAspNet) =>
            {
                if (hasAspNet)
                    spc.AddSource("ValidationEndpointFilter.g.cs", ValidationEndpointFilterSource);
            });

        var targets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ZValidateAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => ExtractValidationInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(targets, static (spc, info) =>
        {
            var source = GenerateValidation(info);
            spc.AddSource($"{info.HintName}.Validation.g.cs", source);

            if (info.IsPartial)
                spc.AddSource($"{info.HintName}.Validation.CodeMap.g.cs", GenerateCodeMap(info));
        });

        // [ImTiredOfCrud] (from ZibStack.NET.UI) -> implies [ZValidate]
        var modelTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZibStack.NET.UI.ImTiredOfCrudAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    // Skip if explicit [ZValidate] exists
                    var hasValidate = ((INamedTypeSymbol)ctx.TargetSymbol).GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == ZValidateAttributeFqn);
                    return hasValidate ? null : ExtractValidationInfo(ctx);
                })
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(modelTargets, static (spc, info) =>
        {
            var source = GenerateValidation(info);
            spc.AddSource($"{info.HintName}.Validation.Model.g.cs", source);

            if (info.IsPartial)
                spc.AddSource($"{info.HintName}.Validation.Model.CodeMap.g.cs", GenerateCodeMap(info));
        });
    }
}
