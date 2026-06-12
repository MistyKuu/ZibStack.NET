using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.TypeGen.Generator;

/// <summary>
/// Populates <see cref="SchemaModel.Endpoints"/> from three sources:
/// <list type="number">
///   <item>Hand-written <c>[ApiController]</c> classes in user source with
///   <c>[HttpGet]</c>/<c>[HttpPost]</c>/<c>[HttpPatch]</c>/<c>[HttpPut]</c>/
///   <c>[HttpDelete]</c> methods. Visible directly via symbol attributes.</item>
///   <item>Hand-written Minimal API calls (<c>app.MapGet("/path", lambda)</c> and
///   relatives) — syntactic scan with literal-pattern + inline-lambda coverage.
///   Dynamic registration (interpolated patterns, handler delegates referenced
///   via fields) is out of MVP scope; those patterns get silently skipped.</item>
///   <item><c>[CrudApi]</c> metadata on emittable classes (synthesis — the
///   generator can't see Dto's generated <c>*.Endpoints.g.cs</c> files due to
///   Roslyn's cross-generator visibility wall, so we reconstruct what Dto
///   would emit from the <c>[CrudApi]</c> + key property pair).</item>
/// </list>
///
/// <para>
/// Dedup: when any native source (controller or minimal api) claims the same
/// (verb, pattern) pair that CrudApi synthesis would also emit, native wins —
/// hand-written code is the ground truth for what the API actually exposes.
/// </para>
/// </summary>
internal static class EndpointDiscovery
{
    private const string ApiControllerAttr = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string RouteAttr = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string NonActionAttr = "Microsoft.AspNetCore.Mvc.NonActionAttribute";
    private const string HttpGetAttr = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";
    private const string HttpPostAttr = "Microsoft.AspNetCore.Mvc.HttpPostAttribute";
    private const string HttpPutAttr = "Microsoft.AspNetCore.Mvc.HttpPutAttribute";
    private const string HttpPatchAttr = "Microsoft.AspNetCore.Mvc.HttpPatchAttribute";
    private const string HttpDeleteAttr = "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute";
    private const string FromRouteAttr = "Microsoft.AspNetCore.Mvc.FromRouteAttribute";
    private const string FromBodyAttr = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";
    private const string FromQueryAttr = "Microsoft.AspNetCore.Mvc.FromQueryAttribute";
    private const string FromHeaderAttr = "Microsoft.AspNetCore.Mvc.FromHeaderAttribute";
    private const string ControllerBaseFqn = "Microsoft.AspNetCore.Mvc.ControllerBase";

    /// <summary>
    /// Main entry: scan native controllers + synthesize CrudApi endpoints,
    /// merging both into <see cref="SchemaModel.Endpoints"/>. Call after
    /// <see cref="SchemaParser.Parse"/> has populated classes.
    /// </summary>
    public static void Populate(SchemaModel model, Compilation compilation)
    {
        ScanControllers(model, compilation);
        ScanMinimalApi(model, compilation);
        SynthesizeFromCrudApi(model);
    }

    // ── [CrudApi] synthesis ─────────────────────────────────────────────────

    /// <summary>
    /// For each class carrying <c>[CrudApi]</c>, add the standard N operations
    /// (list / getById / create / update / delete / bulk*) to
    /// <see cref="SchemaModel.Endpoints"/>. Skips a slot when an endpoint with
    /// the same (verb, pattern) is already in the model — native wins over synth.
    ///
    /// <para>
    /// Idempotent by design: collision detection uses current <c>model.Endpoints</c>
    /// so calling twice adds nothing the second time. The OpenAPI emitter invokes
    /// this on entry (for tests / host paths that build a model directly) — the
    /// generator also calls it via <see cref="Populate"/> and no duplication results.
    /// </para>
    /// </summary>
    public static void SynthesizeFromCrudApi(SchemaModel model)
    {
        var existing = new HashSet<(string Verb, string Pattern)>(
            model.Endpoints.Select(e => (e.Verb, e.Pattern)));

        foreach (var cls in model.Classes)
        {
            if (cls.Crud is null) continue;
            if ((cls.Targets & (TypeTarget.OpenApi | TypeTarget.TanStackQuery)) == 0) continue;

            var route = ResolveRoute(cls);
            var collectionPath = "/" + route.TrimStart('/');
            var keyName = LowerFirst(cls.Crud.KeyProperty);
            var itemPath = collectionPath + "/{" + keyName + "}";
            var ops = cls.Crud.Operations;
            var tag = cls.EmittedName;
            var src = cls.SourceName;
            var keyCSharpType = ResolveKeyCSharpType(cls);

            void Add(EndpointInfo e)
            {
                if (existing.Add((e.Verb, e.Pattern))) model.Endpoints.Add(e);
            }

            if ((ops & CrudOperations.GetList) != 0)
                Add(new EndpointInfo
                {
                    Verb = "get", Pattern = collectionPath, OperationId = $"list{tag}",
                    Tag = tag, IsListEndpoint = true, Source = EndpointSource.CrudApi,
                    ResponseCSharpType = $"PaginatedResponse<{src}>",
                });

            if ((ops & CrudOperations.Create) != 0)
                Add(new EndpointInfo
                {
                    Verb = "post", Pattern = collectionPath, OperationId = $"create{tag}",
                    Tag = tag, Source = EndpointSource.CrudApi, SuccessStatusCode = 201,
                    RequestBodyCSharpType = $"Create{src}Request",
                    ResponseCSharpType = src,
                });

            if ((ops & CrudOperations.GetById) != 0)
                Add(new EndpointInfo
                {
                    Verb = "get", Pattern = itemPath, OperationId = $"get{tag}ById",
                    Tag = tag, Source = EndpointSource.CrudApi, HasNotFoundResponse = true,
                    Parameters =
                    {
                        new EndpointParameter { Name = keyName, Location = ParamLocation.Route,
                            CSharpType = keyCSharpType, Required = true },
                    },
                    ResponseCSharpType = src,
                });

            if ((ops & CrudOperations.Update) != 0)
                Add(new EndpointInfo
                {
                    Verb = "patch", Pattern = itemPath, OperationId = $"update{tag}",
                    Tag = tag, Source = EndpointSource.CrudApi,
                    Parameters =
                    {
                        new EndpointParameter { Name = keyName, Location = ParamLocation.Route,
                            CSharpType = keyCSharpType, Required = true },
                    },
                    RequestBodyCSharpType = $"Update{src}Request",
                    ResponseCSharpType = src,
                });

            if ((ops & CrudOperations.Delete) != 0)
                Add(new EndpointInfo
                {
                    Verb = "delete", Pattern = itemPath, OperationId = $"delete{tag}",
                    Tag = tag, Source = EndpointSource.CrudApi, SuccessStatusCode = 204,
                    Parameters =
                    {
                        new EndpointParameter { Name = keyName, Location = ParamLocation.Route,
                            CSharpType = keyCSharpType, Required = true },
                    },
                });

            if ((ops & CrudOperations.BulkCreate) != 0)
                Add(new EndpointInfo
                {
                    Verb = "post", Pattern = collectionPath + "/bulk",
                    OperationId = $"bulkCreate{tag}", Tag = tag,
                    Source = EndpointSource.CrudApi, SuccessStatusCode = 201,
                    RequestBodyArrayItemCSharpType = $"Create{src}Request",
                    ResponseArrayItemCSharpType = src,
                });

            if ((ops & CrudOperations.BulkDelete) != 0)
                Add(new EndpointInfo
                {
                    Verb = "post", Pattern = collectionPath + "/bulk-delete",
                    OperationId = $"bulkDelete{tag}", Tag = tag,
                    Source = EndpointSource.CrudApi, SuccessStatusCode = 204,
                    RequestBodyArrayItemCSharpType = keyCSharpType,
                });
        }
    }

    private static string ResolveRoute(SchemaClass cls)
    {
        var crud = cls.Crud!;
        if (!string.IsNullOrEmpty(crud.Route)) return crud.Route!;
        var prefix = crud.RoutePrefix?.Trim('/');
        var middle = string.IsNullOrEmpty(prefix) ? "" : prefix + "/";
        return $"api/{middle}{Pluralize(cls.SourceName).ToLowerInvariant()}";
    }

    private static string Pluralize(string name) => name + "s";

    private static string LowerFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    /// <summary>
    /// Returns the C# type of the CrudApi key property on the class. Falls back
    /// to <c>int</c> when the property isn't found — same default as the runtime
    /// <c>[CrudApi]</c> pipeline assumes for key inference.
    /// </summary>
    private static string ResolveKeyCSharpType(SchemaClass cls)
    {
        var key = cls.Properties.FirstOrDefault(p =>
            string.Equals(p.SourceName, cls.Crud!.KeyProperty, System.StringComparison.Ordinal));
        return key?.CSharpTypeFullName ?? "int";
    }

    // ── native controller scan ──────────────────────────────────────────────

    /// <summary>
    /// Walks the compilation for <c>[ApiController]</c> classes (or classes
    /// inheriting <c>ControllerBase</c> — the conventional marker). Extracts
    /// <c>[HttpX]</c> methods into <see cref="EndpointInfo"/> entries.
    /// </summary>
    private static void ScanControllers(SchemaModel model, Compilation compilation)
    {
        var apiControllerSym = compilation.GetTypeByMetadataName(ApiControllerAttr);
        var controllerBaseSym = compilation.GetTypeByMetadataName(ControllerBaseFqn);
        // Fast path — if ASP.NET Core Mvc isn't referenced at all, skip the scan.
        if (apiControllerSym is null && controllerBaseSym is null) return;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model2 = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var decl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model2.GetDeclaredSymbol(decl) is not INamedTypeSymbol sym) continue;
                if (!IsController(sym)) continue;
                ScanControllerSymbol(model, sym);
            }
        }
    }

    private static bool IsController(INamedTypeSymbol sym)
    {
        if (HasAttr(sym, ApiControllerAttr)) return true;
        // Inherits ControllerBase (or Controller which inherits from it).
        for (var t = sym.BaseType; t is not null; t = t.BaseType)
            if (t.ToDisplayString() == ControllerBaseFqn) return true;
        return false;
    }

    private static void ScanControllerSymbol(SchemaModel model, INamedTypeSymbol controller)
    {
        // Class-level route prefix. Substitute [controller] → class name stripped
        // of the "Controller" suffix (conventional ASP.NET Core routing).
        var classRoute = GetRouteTemplate(controller);
        if (classRoute is not null)
            classRoute = classRoute.Replace("[controller]",
                controller.Name.EndsWith("Controller") ? controller.Name.Substring(0, controller.Name.Length - "Controller".Length) : controller.Name);

        var tag = controller.Name.EndsWith("Controller")
            ? controller.Name.Substring(0, controller.Name.Length - "Controller".Length)
            : controller.Name;

        foreach (var member in controller.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;
            if (HasAttr(member, NonActionAttr)) continue;

            var (verb, methodTemplate) = GetHttpVerbAndTemplate(member);
            if (verb is null) continue;

            var fullPattern = CombineRouteSegments(classRoute, methodTemplate);
            var endpoint = new EndpointInfo
            {
                Verb = verb,
                Pattern = NormalizePath(fullPattern),
                OperationId = LowerFirst(member.Name),
                Tag = tag,
                Source = EndpointSource.Controller,
            };

            foreach (var p in member.Parameters)
            {
                // Infrastructure parameters — CancellationToken, ambient DI like
                // services that the handler resolves — don't belong in OpenAPI.
                if (IsInfrastructureParam(p)) continue;
                BindParameter(p, endpoint);
            }

            var (responseType, status) = UnwrapReturnType(member.ReturnType);
            endpoint.ResponseCSharpType = responseType;
            if (status.HasValue) endpoint.SuccessStatusCode = status.Value;

            // Heuristic: list endpoints return PaginatedResponse<T> — match the
            // same shape detection used in emission for emitting PaginatedResponseOf{T}.
            if (responseType is { } r && r.StartsWith("PaginatedResponse<", System.StringComparison.Ordinal))
                endpoint.IsListEndpoint = true;

            AddMissingRouteParameters(endpoint);
            model.Endpoints.Add(endpoint);
        }
    }

    /// <summary>
    /// Reads <c>[Route("template")]</c> on a symbol (class or method). Returns
    /// the template string or <c>null</c> when absent.
    /// </summary>
    private static string? GetRouteTemplate(ISymbol sym)
    {
        var attr = sym.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == RouteAttr);
        if (attr is null) return null;
        return attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s ? s : null;
    }

    /// <summary>
    /// Matches any of the five <c>[HttpX]</c> attributes on a method; returns
    /// the verb + route template (if any). Template can be null for
    /// <c>[HttpGet]</c> (no explicit path), in which case the class-level
    /// <c>[Route]</c> is the full pattern.
    /// </summary>
    private static (string? Verb, string? Template) GetHttpVerbAndTemplate(IMethodSymbol method)
    {
        foreach (var attr in method.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            string? verb = name switch
            {
                HttpGetAttr => "get",
                HttpPostAttr => "post",
                HttpPutAttr => "put",
                HttpPatchAttr => "patch",
                HttpDeleteAttr => "delete",
                _ => null,
            };
            if (verb is null) continue;
            var template = attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s
                ? s : null;
            return (verb, template);
        }
        return (null, null);
    }

    private static string CombineRouteSegments(string? left, string? right)
    {
        var l = (left ?? "").Trim('/');
        var r = (right ?? "").Trim('/');
        if (l.Length == 0) return r;
        if (r.Length == 0) return l;
        return l + "/" + r;
    }

    private static string NormalizePath(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return "/";
        return "/" + pattern.TrimStart('/');
    }

    private static void AddMissingRouteParameters(EndpointInfo endpoint)
    {
        var existing = new HashSet<string>(
            endpoint.Parameters
                .Where(p => p.Location == ParamLocation.Route)
                .Select(p => p.Name),
            System.StringComparer.Ordinal);

        foreach (var routeParam in ExtractRouteParameters(endpoint.Pattern))
        {
            if (!existing.Add(routeParam.Name)) continue;
            endpoint.Parameters.Add(new EndpointParameter
            {
                Name = routeParam.Name,
                Location = ParamLocation.Route,
                CSharpType = routeParam.CSharpType,
                Required = true,
                Description = "Inferred from route template because no handler parameter was bound.",
            });
        }
    }

    private static IEnumerable<(string Name, string CSharpType)> ExtractRouteParameters(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '{') continue;
            var close = pattern.IndexOf('}', i + 1);
            if (close < 0) yield break;

            var raw = pattern.Substring(i + 1, close - i - 1).Trim();
            while (raw.StartsWith("*", System.StringComparison.Ordinal)) raw = raw.Substring(1);
            if (raw.Length == 0)
            {
                i = close;
                continue;
            }

            var colon = raw.IndexOf(':');
            var nameEnd = FirstNonNegative(
                colon,
                raw.IndexOf('='),
                raw.IndexOf('?'));
            var name = (nameEnd >= 0 ? raw.Substring(0, nameEnd) : raw).Trim();
            if (name.Length == 0)
            {
                i = close;
                continue;
            }

            var constraint = "";
            if (colon >= 0 && colon + 1 < raw.Length)
            {
                var constraintStart = colon + 1;
                var constraintEnd = FirstNonNegative(
                    raw.IndexOf(':', constraintStart),
                    raw.IndexOf('=', constraintStart),
                    raw.IndexOf('?', constraintStart));
                constraint = constraintEnd >= 0
                    ? raw.Substring(constraintStart, constraintEnd - constraintStart)
                    : raw.Substring(constraintStart);
            }

            yield return (name, InferRouteConstraintCSharpType(constraint));
            i = close;
        }
    }

    private static int FirstNonNegative(params int[] values)
    {
        var best = -1;
        foreach (var value in values)
        {
            if (value < 0) continue;
            if (best < 0 || value < best) best = value;
        }
        return best;
    }

    private static string InferRouteConstraintCSharpType(string constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return "string";
        return constraint.Trim().ToLowerInvariant() switch
        {
            "bool" or "boolean" => "bool",
            "datetime" => "System.DateTime",
            "decimal" => "decimal",
            "double" => "double",
            "float" => "float",
            "guid" => "System.Guid",
            "int" => "int",
            "long" => "long",
            "short" => "short",
            _ => "string",
        };
    }

    /// <summary>
    /// Binds a controller method parameter to its OpenAPI shape. Explicit
    /// <c>[FromX]</c> attributes win; otherwise ASP.NET Core's default convention:
    /// simple types from route (when the name appears in the template) or query,
    /// complex types from body. We approximate with a simple heuristic since the
    /// full template isn't at hand here — caller passes the pattern's route
    /// placeholders in via a future refinement.
    /// </summary>
    private static void BindParameter(IParameterSymbol param, EndpointInfo endpoint)
    {
        ParamLocation location;
        if (HasAttr(param, FromRouteAttr)) location = ParamLocation.Route;
        else if (HasAttr(param, FromBodyAttr)) location = ParamLocation.Body;
        else if (HasAttr(param, FromQueryAttr)) location = ParamLocation.Query;
        else if (HasAttr(param, FromHeaderAttr)) location = ParamLocation.Header;
        else
        {
            // Fallback convention: param name appearing as `{name}` in pattern → route;
            // simple types → query; complex types → body.
            var isInRoute = endpoint.Pattern.Contains("{" + param.Name + "}")
                || endpoint.Pattern.Contains("{" + param.Name + ":");
            if (isInRoute) location = ParamLocation.Route;
            else if (IsSimpleType(param.Type)) location = ParamLocation.Query;
            else location = ParamLocation.Body;
        }

        if (location == ParamLocation.Body)
        {
            // Only one body allowed. If a subsequent parameter also requests body
            // binding, the second wins (ASP.NET Core picks the last). We mirror that.
            endpoint.RequestBodyCSharpType = param.Type.ToDisplayString();
        }
        else
        {
            endpoint.Parameters.Add(new EndpointParameter
            {
                Name = param.Name,
                Location = location,
                CSharpType = param.Type.ToDisplayString(),
                Required = !IsOptional(param),
            });
        }
    }

    private static bool IsOptional(IParameterSymbol p) =>
        p.HasExplicitDefaultValue || p.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>
    /// Simple vs complex classification matches ASP.NET Core's default binding.
    /// Everything that's a primitive, enum, DateTime/Guid/etc. goes to query/route;
    /// reference types (classes, records, collections) get body binding.
    /// </summary>
    private static bool IsSimpleType(ITypeSymbol t)
    {
        if (t.TypeKind == TypeKind.Enum) return true;
        if (t is INamedTypeSymbol nt && nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return IsSimpleType(nt.TypeArguments[0]);
        return t.SpecialType switch
        {
            SpecialType.System_String or SpecialType.System_Boolean
                or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                or SpecialType.System_DateTime or SpecialType.System_Char => true,
            _ => t.ToDisplayString() switch
            {
                "System.Guid" or "System.DateTimeOffset" or "System.DateOnly"
                    or "System.TimeOnly" or "System.TimeSpan" => true,
                _ => false,
            },
        };
    }

    /// <summary>
    /// Parameters that ASP.NET Core's pipeline injects from DI / ambient state
    /// (CancellationToken, HttpContext, IFormFile etc.) — not part of the
    /// contract. Excluded from OpenAPI emission.
    /// </summary>
    private static bool IsInfrastructureParam(IParameterSymbol p)
    {
        var name = p.Type.ToDisplayString();
        if (name == "System.Threading.CancellationToken") return true;
        if (name == "Microsoft.AspNetCore.Http.HttpContext") return true;
        // Services injected into action methods via [FromServices] — mark explicitly
        // by attribute since heuristics would misclassify domain DTOs.
        if (HasAttr(p, "Microsoft.AspNetCore.Mvc.FromServicesAttribute")) return true;
        return false;
    }

    /// <summary>
    /// Unwraps <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>, then
    /// <c>ActionResult&lt;T&gt;</c> / <c>IActionResult</c>. Returns the
    /// underlying C# type FQN and (if determinable) an explicit success status code.
    /// <c>IActionResult</c> / <c>ActionResult</c> without a generic argument has
    /// no statically-known response shape — return <c>null</c> and let the emitter
    /// emit an empty schema.
    /// </summary>
    private static (string? ResponseType, int? StatusCode) UnwrapReturnType(ITypeSymbol t)
    {
        if (t is INamedTypeSymbol nt)
        {
            var full = nt.OriginalDefinition.ToDisplayString();
            if (full == "System.Threading.Tasks.Task<TResult>" || full == "System.Threading.Tasks.ValueTask<TResult>")
                return UnwrapReturnType(nt.TypeArguments[0]);
            if (full == "Microsoft.AspNetCore.Mvc.ActionResult<TValue>")
                return UnwrapReturnType(nt.TypeArguments[0]);
        }
        var disp = t.ToDisplayString();
        if (disp is "Microsoft.AspNetCore.Mvc.IActionResult"
            or "Microsoft.AspNetCore.Mvc.ActionResult"
            or "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.ValueTask"
            or "void") return (null, null);
        return (disp, null);
    }

    private static bool HasAttr(ISymbol sym, string fqn) =>
        sym.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fqn);

    // ── Minimal API scan ────────────────────────────────────────────────────

    private const string EndpointRouteBuilderFqn = "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
    private const string RouteGroupBuilderFqn = "Microsoft.AspNetCore.Routing.RouteGroupBuilder";

    /// <summary>
    /// Finds <c>app.MapGet("/path", handler)</c> / <c>MapPost</c> / etc.
    /// invocations in user source and adds them as <see cref="EndpointInfo"/>
    /// entries. MVP coverage:
    /// <list type="bullet">
    ///   <item>Literal string route pattern (first arg) — interpolated or
    ///   const-from-field patterns are skipped silently.</item>
    ///   <item>Inline lambda handlers and method-group handlers. Method groups
    ///   require explicit endpoint names so operation IDs stay stable.</item>
    ///   <item><c>MapGroup("/prefix")</c> chain tracking — prefixes prepended
    ///   to the child pattern.</item>
    ///   <item>Parameter binding via <c>[FromX]</c> attributes or convention
    ///   (route placeholder name match, simple→query, complex→body).</item>
    ///   <item>Return type via SemanticModel — unwraps <c>Task&lt;T&gt;</c> and
    ///   <c>ValueTask&lt;T&gt;</c>. <c>IResult</c> return leaves the response
    ///   schema empty (ASP.NET Core's untyped success path).</item>
    ///   <item><c>.Produces&lt;T&gt;()</c> response metadata. When present, the
    ///   produced type is treated as the explicit contract and overrides the
    ///   lambda or method-group return type for generated OpenAPI/TanStack
    ///   response shapes.</item>
    /// </list>
    /// </summary>
    private static void ScanMinimalApi(SchemaModel model, Compilation compilation)
    {
        // Fast-path skip when ASP.NET Core Routing isn't referenced.
        if (compilation.GetTypeByMetadataName(EndpointRouteBuilderFqn) is null) return;

        var existing = new HashSet<(string Verb, string Pattern)>(
            model.Endpoints.Select(e => (e.Verb, e.Pattern)));

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semantic = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var verb = GetMapVerb(inv);
                if (verb is null) continue;

                // Require at least (pattern, handler) args. Minimal API overloads
                // without a pattern (e.g. `.Map(...)` with just a delegate) are out
                // of scope — no route = no path in OpenAPI.
                if (inv.ArgumentList.Arguments.Count < 2) continue;

                var patternLit = GetStringLiteral(inv.ArgumentList.Arguments[0].Expression, semantic);
                if (patternLit is null) continue;  // non-literal pattern → skip

                var handler = inv.ArgumentList.Arguments[1].Expression;

                // Walk left of `.MapX(...)` to collect MapGroup prefix chain +
                // find the root builder (app / MapGroup result / field of type
                // IEndpointRouteBuilder / RouteGroupBuilder).
                var prefix = ResolveGroupChain(inv.Expression, semantic);
                var pattern = NormalizePath(CombineRouteSegments(prefix, patternLit));

                var explicitOperationId = ResolveChainedEndpointMetadata(inv, semantic, "WithName");
                if (handler is not LambdaExpressionSyntax && explicitOperationId is null) continue;

                var operationId = explicitOperationId ?? DeriveMinimalApiOperationId(inv, pattern, verb);
                var tag = ResolveChainedEndpointMetadata(inv, semantic, "WithTags")
                    ?? DeriveMinimalApiTag(pattern);

                var endpoint = new EndpointInfo
                {
                    Verb = verb,
                    Pattern = pattern,
                    OperationId = LowerFirst(operationId),
                    Tag = tag,
                    Source = EndpointSource.MinimalApi,
                };

                if (handler is LambdaExpressionSyntax lambda)
                {
                    BindLambdaParameters(lambda, endpoint, semantic);
                    SetLambdaReturnType(lambda, endpoint, semantic);
                }
                else if (ResolveHandlerMethod(handler, semantic) is { } method)
                {
                    BindMethodParameters(method, endpoint);
                    SetMethodReturnType(method, endpoint);
                }
                else
                {
                    continue;
                }

                if (ResolveChainedProducesResponseType(inv, semantic) is { } producedResponseType)
                    endpoint.ResponseCSharpType = producedResponseType;

                AddMissingRouteParameters(endpoint);
                if (existing.Add((endpoint.Verb, endpoint.Pattern)))
                    model.Endpoints.Add(endpoint);
            }
        }
    }

    /// <summary>
    /// Matches the member-access name on the invocation against the five
    /// Minimal API verb methods. Returns the lowercase verb or <c>null</c>
    /// when the method name isn't one we care about.
    /// </summary>
    private static string? GetMapVerb(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax ma) return null;
        return ma.Name.Identifier.Text switch
        {
            "MapGet" => "get",
            "MapPost" => "post",
            "MapPut" => "put",
            "MapPatch" => "patch",
            "MapDelete" => "delete",
            _ => null,
        };
    }

    /// <summary>
    /// Extracts a string constant from an expression. Handles bare string
    /// literals and <c>nameof</c>/<c>const</c> references resolvable by
    /// Roslyn's constant value provider. Interpolated strings return <c>null</c>.
    /// </summary>
    private static string? GetStringLiteral(ExpressionSyntax expr, SemanticModel sm)
    {
        var cv = sm.GetConstantValue(expr);
        return cv.HasValue ? cv.Value as string : null;
    }

    private static string? ResolveChainedEndpointMetadata(
        InvocationExpressionSyntax mapInvocation,
        SemanticModel sm,
        string methodName)
    {
        SyntaxNode current = mapInvocation;
        while (current.Parent is MemberAccessExpressionSyntax ma
            && ReferenceEquals(ma.Expression, current)
            && ma.Parent is InvocationExpressionSyntax outer)
        {
            if (ma.Name.Identifier.Text == methodName && outer.ArgumentList.Arguments.Count > 0)
            {
                var value = GetStringLiteral(outer.ArgumentList.Arguments[0].Expression, sm);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            current = outer;
        }
        return null;
    }

    /// <summary>
    /// Walks metadata chained after <c>MapGet(...)</c> looking for
    /// <c>.Produces&lt;T&gt;()</c>. ASP.NET treats this as explicit response
    /// metadata, so TypeGen intentionally lets it override the handler's
    /// inferred return type for generated OpenAPI and TanStack response types.
    /// </summary>
    private static string? ResolveChainedProducesResponseType(
        InvocationExpressionSyntax mapInvocation,
        SemanticModel sm)
    {
        SyntaxNode current = mapInvocation;
        while (current.Parent is MemberAccessExpressionSyntax ma
            && ReferenceEquals(ma.Expression, current)
            && ma.Parent is InvocationExpressionSyntax outer)
        {
            if (ma.Name is GenericNameSyntax genericName
                && genericName.Identifier.Text == "Produces"
                && genericName.TypeArgumentList.Arguments.Count > 0)
            {
                var typeInfo = sm.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]);
                if (typeInfo.Type is not null)
                    return typeInfo.Type.ToDisplayString();
            }
            current = outer;
        }
        return null;
    }

    private static IMethodSymbol? ResolveHandlerMethod(ExpressionSyntax handler, SemanticModel sm)
    {
        var symbol = sm.GetSymbolInfo(handler).Symbol;
        if (symbol is IMethodSymbol method) return method;

        foreach (var candidate in sm.GetSymbolInfo(handler).CandidateSymbols.OfType<IMethodSymbol>())
            return candidate;

        return null;
    }

    /// <summary>
    /// Walks left of a <c>.MapGet(...)</c> invocation looking for
    /// <c>MapGroup("/prefix")</c> calls or variables of type
    /// <see cref="RouteGroupBuilderFqn"/>. Collects every literal prefix found
    /// and joins them. Chain forms handled:
    /// <list type="bullet">
    ///   <item><c>app.MapGroup("/api").MapGroup("/v1").MapGet(...)</c></item>
    ///   <item><c>var g = app.MapGroup("/api"); g.MapGet(...)</c> — traced via
    ///   <see cref="SymbolInfo"/> lookup on the identifier.</item>
    /// </list>
    /// Anything it can't decode (field-backed groups, factory methods) just
    /// doesn't contribute a prefix — the endpoint still emits, prefix just
    /// isn't prepended.
    /// </summary>
    private static string ResolveGroupChain(ExpressionSyntax mapCallLeft, SemanticModel sm)
    {
        // mapCallLeft is "app.MapGet" style member access — we want the part
        // LEFT of the dot (i.e. "app" or "app.MapGroup(\"/api\")").
        if (mapCallLeft is not MemberAccessExpressionSyntax ma) return "";
        return ResolveReceiver(ma.Expression, sm);
    }

    private static string ResolveReceiver(ExpressionSyntax receiver, SemanticModel sm)
    {
        // `x.MapGroup("/api")` — peel off the MapGroup layer, recurse left,
        // append this layer's literal prefix.
        if (receiver is InvocationExpressionSyntax nested
            && nested.Expression is MemberAccessExpressionSyntax nestedMa)
        {
            if (nestedMa.Name.Identifier.Text == "MapGroup")
            {
                var inner = ResolveReceiver(nestedMa.Expression, sm);
                var thisPrefix = nested.ArgumentList.Arguments.Count >= 1
                    ? GetStringLiteral(nested.ArgumentList.Arguments[0].Expression, sm)
                    : null;
                return CombineRouteSegments(inner, thisPrefix);
            }

            if (IsEndpointConventionBuilderMetadata(nestedMa.Name.Identifier.Text))
                return ResolveReceiver(nestedMa.Expression, sm);
        }

        // `g.MapGet(...)` where g is a local from `var g = app.MapGroup("/api")` —
        // follow the symbol to its initializer expression.
        if (receiver is IdentifierNameSyntax id)
        {
            var sym = sm.GetSymbolInfo(id).Symbol;
            if (sym is ILocalSymbol local)
            {
                var decl = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (decl is VariableDeclaratorSyntax vd
                    && vd.Initializer?.Value is ExpressionSyntax init)
                    return ResolveReceiver(init, sm);
            }
        }
        return "";
    }

    private static bool IsEndpointConventionBuilderMetadata(string methodName) =>
        methodName is "WithName" or "WithTags" or "WithDisplayName" or "WithDescription"
            or "WithSummary" or "WithGroupName" or "WithOpenApi" or "RequireAuthorization"
            or "AllowAnonymous" or "Produces" or "ProducesProblem" or "Accepts";

    /// <summary>
    /// Builds an operationId for a Minimal API endpoint. Minimal API has no
    /// method name to pull from, so synthesize: <c>{verb}{PascalFromPath}</c>
    /// — e.g. <c>getApiOrdersId</c> for <c>GET /api/orders/{id}</c>. Rough but
    /// stable across rebuilds and unique in practice.
    /// </summary>
    private static string DeriveMinimalApiOperationId(InvocationExpressionSyntax _, string pattern, string verb)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(verb);
        foreach (var seg in pattern.Split('/'))
        {
            if (string.IsNullOrEmpty(seg)) continue;
            // Template placeholder — strip braces, capitalize the name so we
            // get `getOrderById` from `/order/{id}`.
            var clean = seg.TrimStart('{').TrimEnd('}');
            int colon = clean.IndexOf(':');
            if (colon > 0) clean = clean.Substring(0, colon);  // drop route constraint
            if (clean.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(clean[0]));
            if (clean.Length > 1) sb.Append(clean.Substring(1));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Tag from the first non-template path segment ("orders" in "/api/orders/{id}").
    /// Keeps related endpoints grouped in Swagger UI. Falls back to "api" when the
    /// path has only templates / nothing useful.
    /// </summary>
    private static string DeriveMinimalApiTag(string pattern)
    {
        foreach (var seg in pattern.Split('/'))
        {
            if (string.IsNullOrEmpty(seg)) continue;
            if (seg.StartsWith("{")) continue;
            if (seg.Equals("api", System.StringComparison.OrdinalIgnoreCase)) continue;
            // Capitalize the first letter so tags read cleanly.
            return char.ToUpperInvariant(seg[0]) + (seg.Length > 1 ? seg.Substring(1) : "");
        }
        return "api";
    }

    /// <summary>
    /// Walks the lambda's parameter list, resolves each via SemanticModel,
    /// and reuses <see cref="BindParameter"/> so binding rules (<c>[FromX]</c>
    /// + convention) stay in lockstep with the controller scan.
    /// </summary>
    private static void BindLambdaParameters(LambdaExpressionSyntax lambda, EndpointInfo endpoint, SemanticModel sm)
    {
        var paramList = lambda switch
        {
            ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters,
            SimpleLambdaExpressionSyntax simple => new SeparatedSyntaxList<ParameterSyntax>().Add(simple.Parameter),
            _ => default,
        };

        foreach (var p in paramList)
        {
            if (sm.GetDeclaredSymbol(p) is not IParameterSymbol sym) continue;
            if (IsInfrastructureParam(sym)) continue;
            BindParameter(sym, endpoint);
        }
    }

    private static void BindMethodParameters(IMethodSymbol method, EndpointInfo endpoint)
    {
        foreach (var param in method.Parameters)
        {
            if (IsInfrastructureParam(param)) continue;
            BindParameter(param, endpoint);
        }
    }

    private static void SetMethodReturnType(IMethodSymbol method, EndpointInfo endpoint)
    {
        var (responseType, _) = UnwrapReturnType(method.ReturnType);
        endpoint.ResponseCSharpType = responseType;
        if (responseType is "Microsoft.AspNetCore.Http.IResult") endpoint.ResponseCSharpType = null;

        if (responseType is { } r && r.StartsWith("PaginatedResponse<", System.StringComparison.Ordinal))
            endpoint.IsListEndpoint = true;
    }

    /// <summary>
    /// Resolve the lambda's return type via the SemanticModel and stamp the
    /// endpoint's response. <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>
    /// unwrap, <c>IResult</c> / <c>void</c>-ish yields null (no schema).
    /// <c>PaginatedResponse&lt;T&gt;</c> is preserved as-is so the emitter's
    /// pagination recognition fires downstream.
    /// </summary>
    private static void SetLambdaReturnType(LambdaExpressionSyntax lambda, EndpointInfo endpoint, SemanticModel sm)
    {
        // For inline lambdas converted to a delegate (which Minimal API's `Map*`
        // always does), the LambdaExpression's TypeInfo.ConvertedType is the
        // delegate. Easier: get the method symbol for the lambda directly —
        // GetSymbolInfo returns an IMethodSymbol representing the lambda body.
        var sym = sm.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
        if (sym is null) return;

        var (responseType, _) = UnwrapReturnType(sym.ReturnType);
        endpoint.ResponseCSharpType = responseType;

        // IResult / TypedResults / Task<IResult> etc. — not statically typeable
        // for OpenAPI; leave null and the emitter skips the content schema.
        if (responseType is "Microsoft.AspNetCore.Http.IResult") endpoint.ResponseCSharpType = null;

        if (responseType is { } r && r.StartsWith("PaginatedResponse<", System.StringComparison.Ordinal))
            endpoint.IsListEndpoint = true;
    }
}
