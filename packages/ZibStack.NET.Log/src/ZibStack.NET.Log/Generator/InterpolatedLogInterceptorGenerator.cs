using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Log.Generator;

/// <summary>
/// Source generator that intercepts <c>logger.LogXxx($"...")</c> calls and emits
/// zero-allocation typed dispatch via <c>LoggerMessage.Define&lt;T1,...&gt;</c>.
/// Reads the typed slots that the handler stored at the call site (no boxing).
/// </summary>
[Generator]
public sealed class InterpolatedLogInterceptorGenerator : IIncrementalGenerator
{
    private const string ExtensionsFqn = "ZibStack.NET.Log.LoggerStructuredExtensions";

    private static readonly Dictionary<string, string> LevelByMethod = new()
    {
        ["LogTrace"] = "Trace",
        ["LogDebug"] = "Debug",
        ["LogInformation"] = "Information",
        ["LogWarning"] = "Warning",
        ["LogError"] = "Error",
        ["LogCritical"] = "Critical",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateInvocation(node),
                transform: static (ctx, ct) => ParseCallSite(ctx, ct))
            .Where(static cs => cs is not null)
            .Select(static (cs, _) => cs!)
            .Collect();

        // Read fluent ILogConfigurator → b.Interpolation(i => { i.PropertyNameCasing = ... })
        var casing = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var defaults = LogConfiguratorParser.Read(compilation);
            return defaults.PropertyNameCasing ?? 0; // 0 = PascalCase (default), 1 = CamelCase
        });

        context.RegisterSourceOutput(callSites.Combine(casing), (spc, pair) =>
        {
            var (sites, casingValue) = pair;
            if (sites.Length == 0) return;
            bool usePascalCase = casingValue != 1;
            var source = Emit(sites, usePascalCase);
            spc.AddSource("ZibLogStructuredInterceptors.g.cs", source);
        });
    }

    private static bool IsCandidateInvocation(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax m) return false;
        var name = m.Name.Identifier.Text;
        if (!LevelByMethod.ContainsKey(name)) return false;
        // At least one argument must be an interpolated string
        return inv.ArgumentList.Arguments
            .Any(a => a.Expression is InterpolatedStringExpressionSyntax);
    }

    private static InterceptedCallSite? ParseCallSite(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not InvocationExpressionSyntax inv) return null;
        if (inv.Expression is not MemberAccessExpressionSyntax m) return null;
        var methodName = m.Name.Identifier.Text;
        if (!LevelByMethod.TryGetValue(methodName, out var level)) return null;

        var filePath = inv.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath) || filePath.EndsWith(".g.cs")) return null;

        ct.ThrowIfCancellationRequested();

        var symbolInfo = context.SemanticModel.GetSymbolInfo(inv, ct);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) return null;

        // Must be one of our LoggerStructuredExtensions methods
        var containingFqn = methodSymbol.ContainingType?.ToDisplayString();
        if (containingFqn != ExtensionsFqn) return null;

        // Find the interpolated string argument (last positional, after optional Exception)
        InterpolatedStringExpressionSyntax? interpString = null;
        bool hasException = false;
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (arg.Expression is InterpolatedStringExpressionSyntax ise)
            {
                interpString = ise;
                break;
            }
        }
        if (interpString is null) return null;

        // Check if first non-this arg is Exception (we'll pass it through)
        if (inv.ArgumentList.Arguments.Count >= 1 &&
            inv.ArgumentList.Arguments[0].Expression is not InterpolatedStringExpressionSyntax)
        {
            var exprType = context.SemanticModel.GetTypeInfo(
                inv.ArgumentList.Arguments[0].Expression, ct).Type;
            if (exprType != null && IsExceptionType(exprType))
                hasException = true;
        }

        // Parse interpolation: build template and ordered list of args
        var template = new StringBuilder();
        var args = new List<InterpArg>();

        foreach (var part in interpString.Contents)
        {
            switch (part)
            {
                case InterpolatedStringTextSyntax text:
                    // ValueText returns the raw token (e.g. "{{braces}}" stays "{{braces}}"),
                    // which is already in the LoggerMessage.Define template format ({{ = literal {).
                    template.Append(text.TextToken.ValueText);
                    break;

                case InterpolationSyntax interp:
                    var typeInfo = context.SemanticModel.GetTypeInfo(interp.Expression, ct);
                    var type = typeInfo.Type;
                    if (type is null || type is IErrorTypeSymbol) return null;

                    var sanitized = SanitizeName(interp.Expression.ToString());
                    var format = interp.FormatClause?.FormatStringToken.ValueText;

                    // Parse #name override in format specifier
                    if (format != null)
                    {
                        var hi = format.IndexOf('#');
                        if (hi >= 0)
                        {
                            sanitized = format.Substring(hi + 1);
                            format = hi > 0 ? format.Substring(0, hi) : null;
                        }
                    }

                    template.Append('{').Append(sanitized);
                    if (!string.IsNullOrEmpty(format))
                        template.Append(':').Append(format);
                    template.Append('}');

                    var slotKind = ClassifySlot(type);
                    var typeFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    args.Add(new InterpArg(slotKind, typeFqn, sanitized));
                    break;

                default:
                    return null;
            }
        }

        // Bail if too many args (LoggerMessage.Define max 6)
        if (args.Count > 6) return null;

        // Bail if any arg type can't be slot-stored without boxing AND requires LoggerMessage.Define
        // We accept Object kind too — generator will use object slot (some boxing on append, but
        // delegate is still typed). Better than nothing.

        var interceptable = context.SemanticModel.GetInterceptableLocation(inv, ct);
        if (interceptable is null) return null;

        return new InterceptedCallSite(
            interceptable.GetInterceptsLocationAttributeSyntax(),
            level,
            methodName,
            template.ToString(),
            args,
            hasException);
    }

    // ── Type classification ──────────────────────────────────────────────

    private enum SlotKind { Long, Double, Decimal, String, Object }

    private static SlotKind ClassifySlot(ITypeSymbol type)
    {
        // Unwrap nullable value types
        if (type is INamedTypeSymbol named && named.IsGenericType &&
            named.OriginalDefinition?.ToDisplayString() == "System.Nullable<T>")
        {
            type = named.TypeArguments[0];
        }

        return type.SpecialType switch
        {
            SpecialType.System_Int32 => SlotKind.Long,
            SpecialType.System_Int64 => SlotKind.Long,
            SpecialType.System_Boolean => SlotKind.Long,
            SpecialType.System_Byte => SlotKind.Long,
            SpecialType.System_SByte => SlotKind.Long,
            SpecialType.System_Int16 => SlotKind.Long,
            SpecialType.System_UInt16 => SlotKind.Long,
            SpecialType.System_UInt32 => SlotKind.Long,
            SpecialType.System_UInt64 => SlotKind.Long,
            SpecialType.System_Char => SlotKind.Long,
            SpecialType.System_Double => SlotKind.Double,
            SpecialType.System_Single => SlotKind.Double,
            SpecialType.System_Decimal => SlotKind.Decimal,
            SpecialType.System_String => SlotKind.String,
            _ => SlotKind.Object,
        };
    }

    private static bool IsExceptionType(ITypeSymbol type)
    {
        var t = type;
        while (t != null)
        {
            if (t.ToDisplayString() == "System.Exception") return true;
            t = t.BaseType;
        }
        return false;
    }

    // ── Name sanitization (mirrors handler's runtime sanitizer) ──────────

    private static string SanitizeName(string expression)
    {
        if (string.IsNullOrEmpty(expression)) return "_";

        // Fast path: simple identifier
        bool simple = true;
        foreach (var c in expression)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') { simple = false; break; }
        }
        if (simple) return expression;

        var sb = new StringBuilder(expression.Length);
        bool capitalizeNext = false;
        foreach (var c in expression)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else capitalizeNext = sb.Length > 0;
        }
        return sb.Length > 0 ? sb.ToString() : "_";
    }

    // ── Emission ─────────────────────────────────────────────────────────

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static string ApplyPascalCaseToTemplate(string template)
    {
        // Transform property names inside {name} and {name:format} placeholders
        var sb = new StringBuilder(template.Length);
        int i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                sb.Append("{{");
                i += 2;
                continue;
            }
            if (template[i] == '{')
            {
                int close = template.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(template[i]); i++; continue; }
                var inner = template.Substring(i + 1, close - i - 1);
                var colonIdx = inner.IndexOf(':');
                var name = colonIdx >= 0 ? inner.Substring(0, colonIdx) : inner;
                var format = colonIdx >= 0 ? inner.Substring(colonIdx) : "";
                sb.Append('{').Append(ToPascalCase(name)).Append(format).Append('}');
                i = close + 1;
                continue;
            }
            if (template[i] == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append("}}");
                i += 2;
                continue;
            }
            sb.Append(template[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string Emit(ImmutableArray<InterceptedCallSite> sites, bool usePascalCase = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS9270 // InterceptsLocation");
        sb.AppendLine();
        sb.AppendLine("using global::Microsoft.Extensions.Logging;");
        sb.AppendLine("using global::ZibStack.NET.Log;");
        sb.AppendLine();

        // Define InterceptsLocationAttribute (file-scoped sentinel)
        sb.AppendLine("namespace System.Runtime.CompilerServices");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        sb.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
        sb.AppendLine("    {");
        sb.AppendLine("        public InterceptsLocationAttribute(int version, string data) { }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("namespace ZibStack.Generated.Log");
        sb.AppendLine("{");
        sb.AppendLine("    internal static class __ZibLogStructuredInterceptors");
        sb.AppendLine("    {");

        int eventId = 1;
        for (int i = 0; i < sites.Length; i++)
        {
            EmitOne(sb, sites[i], i, ref eventId, usePascalCase);
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitOne(StringBuilder sb, InterceptedCallSite site, int index, ref int eventId, bool usePascalCase = true)
    {
        var args = site.Args;
        var template = usePascalCase ? ApplyPascalCaseToTemplate(site.Template) : site.Template;

        // Map args to typed slot accessors. We need to know the slot index per type.
        var slotIdxByKind = new Dictionary<SlotKind, int>();
        var slotAccessors = new List<string>();
        var typeArgs = new List<string>();
        foreach (var a in args)
        {
            var idx = slotIdxByKind.TryGetValue(a.Slot, out var x) ? x : 0;
            slotIdxByKind[a.Slot] = idx + 1;
            slotAccessors.Add(SlotAccessor(a, idx));
            typeArgs.Add(MessageDefineType(a));
        }

        var levelEnum = $"global::Microsoft.Extensions.Logging.LogLevel.{site.Level}";
        var handlerType = $"global::ZibStack.NET.Log.ZibLog{site.Level}Handler";
        var eventName = $"{site.MethodName}_{index}";
        var delegateField = $"__log_{index}";
        var ev = eventId++;

        // Build cached LoggerMessage.Define delegate field
        var defineGenericArgs = typeArgs.Count == 0 ? "" : "<" + string.Join(", ", typeArgs) + ">";
        var delegateGenericArgs = typeArgs.Count == 0
            ? "<global::Microsoft.Extensions.Logging.ILogger, global::System.Exception?>"
            : "<global::Microsoft.Extensions.Logging.ILogger, " + string.Join(", ", typeArgs) + ", global::System.Exception?>";

        sb.AppendLine($"        private static readonly global::System.Action{delegateGenericArgs} {delegateField} =");
        sb.AppendLine($"            global::Microsoft.Extensions.Logging.LoggerMessage.Define{defineGenericArgs}(");
        sb.AppendLine($"                {levelEnum},");
        sb.AppendLine($"                new global::Microsoft.Extensions.Logging.EventId({ev}, \"{eventName}\"),");
        sb.AppendLine($"                {EscapeStringLiteral(template)});");
        sb.AppendLine();

        // Interceptor method
        sb.AppendLine($"        {site.InterceptsLocationAttributeSyntax}");

        var exParam = site.HasException ? ", global::System.Exception exception" : "";
        var exArg = site.HasException ? "exception" : "null";

        sb.AppendLine($"        internal static void __Intercept_{index}(this global::Microsoft.Extensions.Logging.ILogger logger{exParam}, ref {handlerType} handler)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!handler.IsEnabled) return;");
        var callArgs = slotAccessors.Count == 0 ? "" : ", " + string.Join(", ", slotAccessors);
        sb.AppendLine($"            {delegateField}(logger{callArgs}, {exArg});");
        sb.AppendLine("        }");
    }

    private static string SlotAccessor(InterpArg a, int idx)
    {
        var typeFqn = a.TypeFqn;
        return a.Slot switch
        {
            SlotKind.Long => $"({typeFqn})handler.L{idx}",
            SlotKind.Double => $"({typeFqn})handler.D{idx}",
            SlotKind.Decimal => $"handler.M{idx}",
            SlotKind.String => $"handler.S{idx}!",
            SlotKind.Object => $"({typeFqn})handler.O{idx}!",
            _ => "default!",
        };
    }

    private static string MessageDefineType(InterpArg a)
    {
        // For LoggerMessage.Define<T>, we use the actual type so the delegate is fully typed.
        return a.TypeFqn;
    }

    private static string EscapeStringLiteral(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    // ── Models ───────────────────────────────────────────────────────────

    private sealed class InterceptedCallSite
    {
        public string InterceptsLocationAttributeSyntax { get; }
        public string Level { get; }
        public string MethodName { get; }
        public string Template { get; }
        public List<InterpArg> Args { get; }
        public bool HasException { get; }

        public InterceptedCallSite(string syntax, string level, string methodName, string template, List<InterpArg> args, bool hasException)
        {
            InterceptsLocationAttributeSyntax = syntax;
            Level = level;
            MethodName = methodName;
            Template = template;
            Args = args;
            HasException = hasException;
        }
    }

    private sealed class InterpArg
    {
        public SlotKind Slot { get; }
        public string TypeFqn { get; }
        public string SanitizedName { get; }

        public InterpArg(SlotKind slot, string typeFqn, string sanitizedName)
        {
            Slot = slot;
            TypeFqn = typeFqn;
            SanitizedName = sanitizedName;
        }
    }
}
