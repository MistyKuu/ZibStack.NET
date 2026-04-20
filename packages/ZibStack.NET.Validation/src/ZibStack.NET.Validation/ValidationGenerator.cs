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

        // Emit ASP.NET endpoint filter only when Microsoft.AspNetCore.Http is referenced
        context.RegisterSourceOutput(
            context.CompilationProvider.Select(static (comp, _) =>
                comp.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Results") is not null),
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
