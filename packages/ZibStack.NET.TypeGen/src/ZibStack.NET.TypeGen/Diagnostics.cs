using Microsoft.CodeAnalysis;

namespace ZibStack.NET.TypeGen.Generator;

/// <summary>
/// TypeGen diagnostic IDs (TG0001+). All under category <c>"ZibStack.TypeGen"</c>
/// for consistent suppression / editorconfig grouping.
/// </summary>
internal static class TypeGenDiagnostics
{
    private const string Category = "ZibStack.TypeGen";

    public const string NoTargetsId = "TG0001";
    public static readonly DiagnosticDescriptor NoTargets = new(
        NoTargetsId,
        title: "[GenerateTypes] specifies no Targets",
        messageFormat: "[GenerateTypes] on '{0}' has Targets = TypeTarget.None — nothing will be emitted. Specify at least one (TypeScript / OpenApi).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string UnsupportedTypeId = "TG0002";
    public static readonly DiagnosticDescriptor UnsupportedType = new(
        UnsupportedTypeId,
        title: "Unsupported property type for emitted target",
        messageFormat: "Property '{0}.{1}' has C# type '{2}' which the {3} emitter cannot translate. Add [TsType(\"...\")] / [OpenApiProperty(...)] to override, or [TsIgnore] / [OpenApiIgnore] to skip this property.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string MultipleConfiguratorsId = "TG0010";
    public static readonly DiagnosticDescriptor MultipleConfigurators = new(
        MultipleConfiguratorsId,
        title: "Multiple ITypeGenConfigurator implementations in this project",
        messageFormat: "Found {0} ITypeGenConfigurator implementations: {1}. Only one configurator per project is allowed — pick one and remove (or `internal`-hide) the others.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string InvalidOutputDirId = "TG0011";
    public static readonly DiagnosticDescriptor InvalidOutputDir = new(
        InvalidOutputDirId,
        title: "Invalid OutputDir on [GenerateTypes]",
        messageFormat: "OutputDir '{0}' on [GenerateTypes] for '{1}' is empty or null. Specify a valid relative or absolute path (e.g. \"../client/src/api\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public const string UnknownConfiguratorCallId = "TG0012";
    public static readonly DiagnosticDescriptor UnknownConfiguratorCall = new(
        UnknownConfiguratorCallId,
        title: "Unrecognized configurator DSL call",
        messageFormat: "TypeGen configurator doesn't recognize '{0}'. Only TypeScript(), OpenApi(), ForType<T>() and their chained builders (.TsName, .OpenApiName, .OutputDir, .Ignore, .TsIgnore, .OpenApiIgnore) are parsed at compile time.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string NonLiteralArgumentId = "TG0013";
    public static readonly DiagnosticDescriptor NonLiteralArgument = new(
        NonLiteralArgumentId,
        title: "Configurator argument must be a compile-time constant",
        messageFormat: "Argument for '{0}' isn't a literal or constant. The configurator is parsed at compile time — use string literals, enum members, or const fields.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public const string GenericTypeId = "TG0003";
    public static readonly DiagnosticDescriptor GenericType = new(
        GenericTypeId,
        title: "Generic types are not yet supported",
        messageFormat: "[GenerateTypes] on generic type '{0}' is not supported in the MVP. Apply to closed/non-generic types only.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
