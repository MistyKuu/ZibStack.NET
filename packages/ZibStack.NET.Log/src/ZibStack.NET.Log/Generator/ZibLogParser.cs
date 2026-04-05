using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Log.Generator;

internal static class ZibLogParser
{
    private const string ZibLogAttributeName = "ZibStack.NET.Log.ZibLogAttribute";
    private const string LogAttributeName = "ZibStack.NET.Log.LogAttribute";
    private const string NoLogAttributeName = "ZibStack.NET.Log.NoLogAttribute";
    private const string SensitiveAttributeName = "ZibStack.NET.Log.SensitiveAttribute";
    private const string ILoggerFullName = "Microsoft.Extensions.Logging.ILogger";
    private const string ILoggerGenericFullName = "Microsoft.Extensions.Logging.ILogger`1";

    public static ClassModel? ParseClass(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not ClassDeclarationSyntax classDecl)
            return null;

        var classSymbol = context.TargetSymbol as INamedTypeSymbol;
        if (classSymbol is null)
            return null;

        ct.ThrowIfCancellationRequested();

        // Extract ZibLog attribute data
        string? loggerFieldOverride = null;
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ZibLogAttributeName)
            {
                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg.Key == "LoggerField" && namedArg.Value.Value is string fieldName)
                    {
                        loggerFieldOverride = fieldName;
                    }
                }
            }
        }

        // Find ILogger field
        var (loggerFieldName, loggerFieldType) = FindLoggerField(classSymbol, loggerFieldOverride);
        if (loggerFieldName is null || loggerFieldType is null)
            return null;

        // Find methods with [Log]
        var methods = new List<MethodModel>();
        foreach (var member in classSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol methodSymbol)
                continue;

            var logAttr = methodSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == LogAttributeName);

            if (logAttr is null)
                continue;

            var methodModel = ParseMethod(methodSymbol, logAttr);
            if (methodModel is not null)
                methods.Add(methodModel);
        }

        if (methods.Count == 0)
            return null;

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : classSymbol.ContainingNamespace.ToDisplayString();

        return new ClassModel(
            ns,
            classSymbol.Name,
            loggerFieldName,
            loggerFieldType,
            methods);
    }

    public static (string? fieldName, string? fieldType) FindLoggerField(
        INamedTypeSymbol classSymbol, string? loggerFieldOverride)
    {
        var loggerFields = new List<(string name, string type)>();

        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IFieldSymbol field)
                continue;

            var fieldType = field.Type;

            if (IsILogger(fieldType))
            {
                loggerFields.Add((field.Name, fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }

        if (loggerFieldOverride is not null)
        {
            var match = loggerFields.FirstOrDefault(f => f.name == loggerFieldOverride);
            return match.name is not null ? (match.name, match.type) : (null, null);
        }

        return loggerFields.Count == 1 ? loggerFields[0] : (null, null);
    }

    public static ImmutableArray<Diagnostic> GetDiagnostics(
        GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetNode is not ClassDeclarationSyntax classDecl)
            return ImmutableArray<Diagnostic>.Empty;

        var classSymbol = context.TargetSymbol as INamedTypeSymbol;
        if (classSymbol is null)
            return ImmutableArray<Diagnostic>.Empty;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // Extract logger field override
        string? loggerFieldOverride = null;
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == ZibLogAttributeName)
            {
                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg.Key == "LoggerField" && namedArg.Value.Value is string fieldName)
                        loggerFieldOverride = fieldName;
                }
            }
        }

        // Check logger field
        var loggerFields = new List<string>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IFieldSymbol field && IsILogger(field.Type))
                loggerFields.Add(field.Name);
        }

        if (loggerFieldOverride is not null)
        {
            if (!loggerFields.Contains(loggerFieldOverride))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SpecifiedLoggerFieldNotFound,
                    classDecl.Identifier.GetLocation(),
                    loggerFieldOverride, classSymbol.Name));
            }
        }
        else if (loggerFields.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.NoLoggerField,
                classDecl.Identifier.GetLocation(),
                classSymbol.Name));
        }
        else if (loggerFields.Count > 1)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.MultipleLoggerFields,
                classDecl.Identifier.GetLocation(),
                classSymbol.Name, string.Join(", ", loggerFields)));
        }

        // Check for static methods with [Log]
        foreach (var member in classSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is IMethodSymbol method && method.IsStatic)
            {
                var hasLog = method.GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == LogAttributeName);
                if (hasLog)
                {
                    var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct);
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.StaticMethodNotSupported,
                        syntax?.GetLocation() ?? classDecl.GetLocation(),
                        method.Name));
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    private static MethodModel? ParseMethod(IMethodSymbol method, AttributeData logAttr)
    {
        if (method.IsStatic)
            return null;

        // Parse LogAttribute properties
        int entryExitLevel = 2; // Information
        int exceptionLevel = 4; // Error
        bool logParameters = true;
        bool logReturnValue = true;
        bool measureElapsed = true;
        string? entryMessage = null;
        string? exitMessage = null;
        string? exceptionMessage = null;
        int objectLogging = 1; // Destructure

        foreach (var namedArg in logAttr.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "EntryExitLevel" when namedArg.Value.Value is int level:
                    entryExitLevel = level;
                    break;
                case "ExceptionLevel" when namedArg.Value.Value is int exLevel:
                    exceptionLevel = exLevel;
                    break;
                case "LogParameters" when namedArg.Value.Value is bool lp:
                    logParameters = lp;
                    break;
                case "LogReturnValue" when namedArg.Value.Value is bool lrv:
                    logReturnValue = lrv;
                    break;
                case "MeasureElapsed" when namedArg.Value.Value is bool me:
                    measureElapsed = me;
                    break;
                case "EntryMessage" when namedArg.Value.Value is string em:
                    entryMessage = em;
                    break;
                case "ExitMessage" when namedArg.Value.Value is string exm:
                    exitMessage = exm;
                    break;
                case "ExceptionMessage" when namedArg.Value.Value is string excm:
                    exceptionMessage = excm;
                    break;
                case "ObjectLogging" when namedArg.Value.Value is int ol:
                    objectLogging = ol;
                    break;
            }
        }

        // Parse parameters
        var parameters = new List<ParameterModel>();
        foreach (var param in method.Parameters)
        {
            bool isNoLog = param.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == NoLogAttributeName);
            bool isSensitive = param.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == SensitiveAttributeName);
            bool isComplex = IsComplexType(param.Type);

            // Check properties for [Sensitive]/[NoLog] on complex types
            SanitizedTypeModel? sanitizedType = null;
            if (isComplex && !isNoLog && !isSensitive)
                sanitizedType = ParseTypeProperties(param.Type);

            parameters.Add(new ParameterModel(
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                isNoLog,
                isSensitive,
                isComplex,
                sanitizedType));
        }

        // Determine async and return type
        var returnType = method.ReturnType;
        bool isAsync = false;
        bool returnsVoid = returnType.SpecialType == SpecialType.System_Void;
        string returnTypeStr = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (returnType is INamedTypeSymbol namedReturn)
        {
            var fullName = namedReturn.ConstructedFrom.ToDisplayString();
            isAsync = fullName is "System.Threading.Tasks.Task"
                or "System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask"
                or "System.Threading.Tasks.ValueTask<TResult>";

            if (isAsync && !namedReturn.IsGenericType)
            {
                // Task or ValueTask (no generic parameter) - treat as void for return logging
                returnsVoid = true;
            }
        }

        var accessibility = method.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => "public"
        };

        // Determine if return type is complex (for object logging)
        var actualReturnType = returnType;
        if (isAsync && returnType is INamedTypeSymbol asyncReturn && asyncReturn.IsGenericType)
        {
            actualReturnType = asyncReturn.TypeArguments[0];
        }
        bool hasComplexReturnType = !returnsVoid && IsComplexType(actualReturnType);

        // Check for [return: Sensitive] and [return: NoLog]
        var returnAttrs = method.GetReturnTypeAttributes();
        bool sensitiveReturn = returnAttrs
            .Any(a => a.AttributeClass?.ToDisplayString() == SensitiveAttributeName);
        bool noLogReturn = returnAttrs
            .Any(a => a.AttributeClass?.ToDisplayString() == NoLogAttributeName);

        // Check return type properties for [Sensitive]/[NoLog]
        SanitizedTypeModel? sanitizedReturnType = null;
        if (hasComplexReturnType && !sensitiveReturn && !noLogReturn)
            sanitizedReturnType = ParseTypeProperties(actualReturnType);

        return new MethodModel(
            method.Name,
            returnTypeStr,
            isAsync,
            returnsVoid,
            accessibility,
            parameters,
            entryExitLevel,
            exceptionLevel,
            logParameters,
            logReturnValue,
            measureElapsed,
            entryMessage,
            exitMessage,
            exceptionMessage,
            objectLogging,
            hasComplexReturnType,
            sensitiveReturn,
            noLogReturn,
            sanitizedReturnType);
    }

    private static bool IsComplexType(ITypeSymbol type)
    {
        // Primitives, string, enums, well-known value types are "simple"
        if (type.SpecialType != SpecialType.None)
            return false; // int, string, bool, double, etc.

        if (type.TypeKind == TypeKind.Enum)
            return false;

        var fullName = type.ToDisplayString();
        return fullName is not (
            "System.DateTime" or
            "System.DateTimeOffset" or
            "System.TimeSpan" or
            "System.Guid" or
            "System.Uri" or
            "System.DateOnly" or
            "System.TimeOnly");
    }

    private const int MaxPropertyDepth = 5;

    /// <summary>
    /// Parses properties of a complex type for [Sensitive]/[NoLog] attributes, recursively.
    /// Returns null if the type has no decorated properties at any depth.
    /// </summary>
    public static SanitizedTypeModel? ParseTypeProperties(ITypeSymbol type, HashSet<string>? visited = null, int depth = 0)
    {
        if (depth >= MaxPropertyDepth)
            return null;

        if (!IsComplexType(type))
            return null;

        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Prevent infinite recursion on circular references
        visited ??= new HashSet<string>();
        if (!visited.Add(fullName))
            return null;

        var properties = new List<TypePropertyModel>();
        bool hasDecoratedProperty = false;

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.IsStatic || prop.IsIndexer || prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (prop.GetMethod is null)
                continue;

            var attrs = prop.GetAttributes();
            bool isSensitive = attrs.Any(a => a.AttributeClass?.ToDisplayString() == SensitiveAttributeName);
            bool isNoLog = attrs.Any(a => a.AttributeClass?.ToDisplayString() == NoLogAttributeName);
            bool isComplex = IsComplexType(prop.Type);

            IReadOnlyList<TypePropertyModel>? nestedProps = null;
            if (isComplex && !isSensitive && !isNoLog)
            {
                var nestedModel = ParseTypeProperties(prop.Type, visited, depth + 1);
                if (nestedModel != null)
                {
                    nestedProps = nestedModel.Properties;
                    hasDecoratedProperty = true;
                }
            }

            if (isSensitive || isNoLog)
                hasDecoratedProperty = true;

            properties.Add(new TypePropertyModel(
                prop.Name,
                prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                isSensitive,
                isNoLog,
                isComplex,
                nestedProps));
        }

        visited.Remove(fullName);

        if (!hasDecoratedProperty)
            return null;

        // Safe name for generated method: global::Ns.Type → Ns_Type
        var safeName = fullName
            .Replace("global::", "")
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "");

        return new SanitizedTypeModel(fullName, safeName, properties);
    }

    private static bool IsILogger(ITypeSymbol type)
    {
        // Check the non-generic ILogger
        var fullName = type.ToDisplayString();
        if (fullName == ILoggerFullName)
            return true;

        // Check generic ILogger<T> - compare the original unbound definition
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDef = namedType.OriginalDefinition;
            var metadataName = originalDef.ContainingNamespace + "." + originalDef.MetadataName;
            if (metadataName == ILoggerGenericFullName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an invocation expression is a call to a method marked with [Log]
    /// on a class marked with [ZibLog]. Returns the call site info if so.
    /// Uses the new InterceptableLocation API (Roslyn 4.12+).
    /// </summary>
    public static CallSiteModel? ParseCallSite(
        GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return null;

        // Skip generated files to prevent recursion
        var filePath = invocation.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath) || filePath.EndsWith(".g.cs"))
            return null;

        ct.ThrowIfCancellationRequested();

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return null;

        // Check if method has [Log]
        var hasLog = methodSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == LogAttributeName);
        if (!hasLog)
            return null;

        // Check if containing class has [ZibLog]
        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
            return null;

        var hasZibLog = containingType.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == ZibLogAttributeName);
        if (!hasZibLog)
            return null;

        // Use the new InterceptableLocation API
        var interceptableLocation = context.SemanticModel.GetInterceptableLocation(invocation, ct);
        if (interceptableLocation is null)
            return null;

        var attributeSyntax = interceptableLocation.GetInterceptsLocationAttributeSyntax();

        var containingNs = containingType.ContainingNamespace.IsGlobalNamespace
            ? ""
            : containingType.ContainingNamespace.ToDisplayString();

        return new CallSiteModel(
            attributeSyntax,
            methodSymbol.Name,
            containingNs,
            containingType.Name);
    }
}
