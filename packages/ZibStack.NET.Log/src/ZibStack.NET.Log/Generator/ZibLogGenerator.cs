using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZibStack.NET.Log.Attributes;

namespace ZibStack.NET.Log.Generator;

[Generator]
public sealed class ZibLogGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Attributes are provided by ZibStack.NET.Log.Abstractions package.
        // No PostInitializationOutput injection — this ensures attributes are in IL
        // and visible across project boundaries for cross-project interception.

        // Step 1: Find all classes with [ZibLog] and parse them
        var classModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZibStack.NET.Log.ZibLogAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ZibLogParser.ParseClass(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Step 2b: Report diagnostics for classes with [ZibLog]
        var diagnostics = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZibStack.NET.Log.ZibLogAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ZibLogParser.GetDiagnostics(ctx, ct))
            .Where(static d => !d.IsEmpty);

        context.RegisterSourceOutput(diagnostics, static (spc, diags) =>
        {
            foreach (var diag in diags)
            {
                spc.ReportDiagnostic(diag);
            }
        });

        // Step 3: Find all call-sites that invoke [Log] methods
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax,
                transform: static (ctx, ct) => ZibLogParser.ParseCallSite(ctx, ct))
            .Where(static cs => cs is not null)
            .Select(static (cs, _) => cs!);

        // Collect call sites into a list
        var callSiteCollection = callSites.Collect();

        // Step 4: Combine class models with call sites and emit
        var combined = classModels.Combine(callSiteCollection);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (classModel, allCallSites) = pair;

            // Filter call sites for this class
            var relevantCallSites = allCallSites
                .Where(cs => cs.ContainingClassName == classModel.ClassName
                    && cs.ContainingClassNamespace == classModel.Namespace)
                .ToList();

            if (relevantCallSites.Count == 0)
                return;

            var source = ZibLogEmitter.Emit(classModel, relevantCallSites);
            spc.AddSource($"{classModel.ClassName}_ZibLog.g.cs", source);
        });
    }
}
