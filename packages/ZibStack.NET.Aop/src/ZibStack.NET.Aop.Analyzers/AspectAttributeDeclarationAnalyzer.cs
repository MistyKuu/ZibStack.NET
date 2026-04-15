using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Validates declarations of custom <c>AspectAttribute</c> subclasses themselves:
/// every aspect must point to a real handler implementation via <c>[AspectHandler]</c>,
/// and that handler must implement one of the runtime handler interfaces.
///
/// These errors are reported on the attribute declaration (or on the [AspectHandler]
/// annotation itself), not on the methods using the aspect — that's where the bug is.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AspectAttributeDeclarationAnalyzer : DiagnosticAnalyzer
{
    private const string AspectAttributeFullName = "ZibStack.NET.Aop.AspectAttribute";
    private const string AspectHandlerAttributeFullName = "ZibStack.NET.Aop.AspectHandlerAttribute";

    private static readonly string[] HandlerInterfaceFullNames =
    {
        "ZibStack.NET.Aop.IAspectHandler",
        "ZibStack.NET.Aop.IAsyncAspectHandler",
        "ZibStack.NET.Aop.IAroundAspectHandler",
        "ZibStack.NET.Aop.IAsyncAroundAspectHandler",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Diagnostics.HandlerTypeMismatch,
            Diagnostics.MissingHandlerAttribute);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;

        // Only interested in concrete subclasses of AspectAttribute.
        if (type.IsAbstract) return;
        if (!DerivesFromAspectAttribute(type)) return;
        // Skip the base type itself.
        if (type.ToDisplayString() == AspectAttributeFullName) return;

        var handlerAttr = type.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AspectHandlerAttributeFullName);

        if (handlerAttr is null)
        {
            // Attribute class with no [AspectHandler] — generator can't wire it up.
            ReportOnTypeName(ctx, Diagnostics.MissingHandlerAttribute, type, type.Name);
            return;
        }

        // [AspectHandler(typeof(X))] takes a single Type argument. If it's null/missing,
        // there's a separate compile error already; nothing to add.
        if (handlerAttr.ConstructorArguments.Length == 0) return;
        var handlerTypeArg = handlerAttr.ConstructorArguments[0];
        if (handlerTypeArg.Value is not INamedTypeSymbol handlerType) return;

        if (!ImplementsAnyHandlerInterface(handlerType))
        {
            // Report on the [AspectHandler(...)] usage if we can locate it; otherwise on the type.
            var loc = handlerAttr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                      ?? type.Locations.FirstOrDefault(l => l.IsInSource)
                      ?? type.Locations.FirstOrDefault();
            if (loc is null) return;
            ctx.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HandlerTypeMismatch,
                loc,
                handlerType.ToDisplayString()));
        }
    }

    private static void ReportOnTypeName(SymbolAnalysisContext ctx, DiagnosticDescriptor rule, INamedTypeSymbol type, params object[] args)
    {
        var loc = type.Locations.FirstOrDefault(l => l.IsInSource) ?? type.Locations.FirstOrDefault();
        if (loc is null) return;
        ctx.ReportDiagnostic(Diagnostic.Create(rule, loc, args));
    }

    private static bool DerivesFromAspectAttribute(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == AspectAttributeFullName)
                return true;
        }
        return false;
    }

    private static bool ImplementsAnyHandlerInterface(INamedTypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            // Match both closed and open generic forms (IAroundAspectHandler<T> etc.).
            var name = iface.OriginalDefinition.ToDisplayString();
            // Strip generic arity to match e.g. "IAroundAspectHandler<TResult>" → "IAroundAspectHandler".
            var nameWithoutGen = name.Contains('<') ? name.Substring(0, name.IndexOf('<')) : name;
            foreach (var known in HandlerInterfaceFullNames)
            {
                if (name == known || nameWithoutGen == known) return true;
            }
        }
        return false;
    }
}
