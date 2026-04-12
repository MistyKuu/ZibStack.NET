using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.UI;

[Generator]
public sealed partial class UiGenerator : IIncrementalGenerator
{
    // ─── FQN constants ───────────────────────────────────────────────────

    // Class-level
    private const string ImTiredOfCrudAttributeFqn = "ZibStack.NET.UI.ImTiredOfCrudAttribute";
    private const string CrudApiAttributeFqn = "ZibStack.NET.Dto.CrudApiAttribute";
    private const string UiFormAttributeFqn = "ZibStack.NET.UI.UiFormAttribute";
    private const string UiFormGroupAttributeFqn = "ZibStack.NET.UI.UiFormGroupAttribute";
    private const string UiTableAttributeFqn = "ZibStack.NET.UI.UiTableAttribute";

    // Property-level — form
    private const string UiFormFieldAttributeFqn = "ZibStack.NET.UI.UiFormFieldAttribute";
    private const string UiFormIgnoreAttributeFqn = "ZibStack.NET.UI.UiFormIgnoreAttribute";
    private const string UiFormHiddenAttributeFqn = "ZibStack.NET.UI.UiFormHiddenAttribute";
    private const string UiFormOrderAttributeFqn = "ZibStack.NET.UI.UiFormOrderAttribute";
    private const string UiFormReadOnlyAttributeFqn = "ZibStack.NET.UI.UiFormReadOnlyAttribute";
    private const string UiFormDisabledAttributeFqn = "ZibStack.NET.UI.UiFormDisabledAttribute";
    private const string UiFormSectionAttributeFqn = "ZibStack.NET.UI.UiFormSectionAttribute";
    private const string UiFormConditionalAttributeFqn = "ZibStack.NET.UI.UiFormConditionalAttribute";

    // Property-level — table
    private const string UiTableColumnAttributeFqn = "ZibStack.NET.UI.UiTableColumnAttribute";
    private const string UiTableIgnoreAttributeFqn = "ZibStack.NET.UI.UiTableIgnoreAttribute";

    private const string RowActionAttributeFqn = "ZibStack.NET.UI.RowActionAttribute";
    private const string ToolbarActionAttributeFqn = "ZibStack.NET.UI.ToolbarActionAttribute";
    private const string PermissionAttributeFqn = "ZibStack.NET.UI.PermissionAttribute";
    private const string ColumnPermissionAttributeFqn = "ZibStack.NET.UI.ColumnPermissionAttribute";
    private const string DataFilterAttributeFqn = "ZibStack.NET.UI.DataFilterAttribute";

    // ERP: property-level
    private const string ComputedAttributeFqn = "ZibStack.NET.UI.ComputedAttribute";
    private const string ColumnStyleAttributeFqn = "ZibStack.NET.UI.ColumnStyleAttribute";

    // Relationship attributes (defined in ZibStack.NET.Core)
    private const string OneToManyAttributeFqn = "ZibStack.NET.Core.OneToManyAttribute";
    private const string OneToOneAttributeFqn = "ZibStack.NET.Core.OneToOneAttribute";
    private const string EntityAttributeFqn = "ZibStack.NET.Core.EntityAttribute";

    // UI control hints
    private const string TextAreaAttributeFqn = "ZibStack.NET.UI.TextAreaAttribute";
    private const string SelectAttributeFqn = "ZibStack.NET.UI.SelectAttribute";
    private const string RadioGroupAttributeFqn = "ZibStack.NET.UI.RadioGroupAttribute";
    private const string CheckboxAttributeFqn = "ZibStack.NET.UI.CheckboxAttribute";
    private const string DatePickerAttributeFqn = "ZibStack.NET.UI.DatePickerAttribute";
    private const string TimePickerAttributeFqn = "ZibStack.NET.UI.TimePickerAttribute";
    private const string DateTimePickerAttributeFqn = "ZibStack.NET.UI.DateTimePickerAttribute";
    private const string FilePickerAttributeFqn = "ZibStack.NET.UI.FilePickerAttribute";
    private const string ColorPickerAttributeFqn = "ZibStack.NET.UI.ColorPickerAttribute";
    private const string RichTextAttributeFqn = "ZibStack.NET.UI.RichTextAttribute";
    private const string SliderAttributeFqn = "ZibStack.NET.UI.SliderAttribute";
    private const string PasswordInputAttributeFqn = "ZibStack.NET.UI.PasswordInputAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ─── Post-init: inject attributes + runtime types ────────────────
        context.RegisterPostInitializationOutput(static ctx =>
        {
            // Form attributes
            ctx.AddSource("UiFormAttribute.g.cs", UiFormAttributeSource);
            ctx.AddSource("UiFormGroupAttribute.g.cs", UiFormGroupAttributeSource);
            ctx.AddSource("UiFormFieldAttribute.g.cs", UiFormFieldAttributeSource);
            ctx.AddSource("UiFormIgnoreAttribute.g.cs", UiFormIgnoreAttributeSource);
            ctx.AddSource("UiFormHiddenAttribute.g.cs", UiFormHiddenAttributeSource);
            ctx.AddSource("UiFormOrderAttribute.g.cs", UiFormOrderAttributeSource);
            ctx.AddSource("UiFormReadOnlyAttribute.g.cs", UiFormReadOnlyAttributeSource);
            ctx.AddSource("UiFormDisabledAttribute.g.cs", UiFormDisabledAttributeSource);
            ctx.AddSource("UiFormSectionAttribute.g.cs", UiFormSectionAttributeSource);
            ctx.AddSource("UiFormConditionalAttribute.g.cs", UiFormConditionalAttributeSource);

            // Table attributes
            ctx.AddSource("UiTableAttribute.g.cs", UiTableAttributeSource);
            ctx.AddSource("UiTableColumnAttribute.g.cs", UiTableColumnAttributeSource);
            ctx.AddSource("UiTableIgnoreAttribute.g.cs", UiTableIgnoreAttributeSource);

            // UI control hint attributes
            ctx.AddSource("TextAreaAttribute.g.cs", TextAreaAttributeSource);
            ctx.AddSource("SelectAttribute.g.cs", SelectAttributeSource);
            ctx.AddSource("RadioGroupAttribute.g.cs", RadioGroupAttributeSource);
            ctx.AddSource("CheckboxAttribute.g.cs", CheckboxAttributeSource);
            ctx.AddSource("DatePickerAttribute.g.cs", DatePickerAttributeSource);
            ctx.AddSource("TimePickerAttribute.g.cs", TimePickerAttributeSource);
            ctx.AddSource("DateTimePickerAttribute.g.cs", DateTimePickerAttributeSource);
            ctx.AddSource("FilePickerAttribute.g.cs", FilePickerAttributeSource);
            ctx.AddSource("ColorPickerAttribute.g.cs", ColorPickerAttributeSource);
            ctx.AddSource("RichTextAttribute.g.cs", RichTextAttributeSource);
            ctx.AddSource("SliderAttribute.g.cs", SliderAttributeSource);
            ctx.AddSource("PasswordInputAttribute.g.cs", PasswordInputAttributeSource);

            // ERP attributes
            // Note: OneToMany, OneToOne, Entity moved to ZibStack.NET.Core
            ctx.AddSource("RowActionAttribute.g.cs", RowActionAttributeSource);
            ctx.AddSource("ToolbarActionAttribute.g.cs", ToolbarActionAttributeSource);
            ctx.AddSource("PermissionAttribute.g.cs", PermissionAttributeSource);
            ctx.AddSource("ColumnPermissionAttribute.g.cs", ColumnPermissionAttributeSource);
            ctx.AddSource("DataFilterAttribute.g.cs", DataFilterAttributeSource);
            ctx.AddSource("ComputedAttribute.g.cs", ComputedAttributeSource);
            ctx.AddSource("ColumnStyleAttribute.g.cs", ColumnStyleAttributeSource);
            ctx.AddSource("ImTiredOfCrudAttribute.g.cs", ImTiredOfCrudAttributeSource);

            // Runtime types
            ctx.AddSource("FormDescriptor.g.cs", FormDescriptorSource);
            ctx.AddSource("TableDescriptor.g.cs", TableDescriptorSource);
        });

        // ─── Form pipeline ──────────────────────────────────────────────
        var formTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UiFormAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => ExtractFormInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(formTargets, static (spc, info) =>
        {
            spc.AddSource($"{info.HintName}.Form.g.cs", GenerateFormDescriptor(info));
            spc.AddSource($"{info.HintName}.FormJson.g.cs", GenerateFormJson(info));

            if (info.IsPartial)
                spc.AddSource($"{info.HintName}.Ui.Form.CodeMap.g.cs", GenerateFormCodeMap(info));
        });

        // ─── Table pipeline ─────────────────────────────────────────────
        var tableTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UiTableAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => ExtractTableInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(tableTargets, static (spc, info) =>
        {
            spc.AddSource($"{info.HintName}.Table.g.cs", GenerateTableDescriptor(info));
            spc.AddSource($"{info.HintName}.TableJson.g.cs", GenerateTableJson(info));

            if (info.IsPartial)
                spc.AddSource($"{info.HintName}.Ui.Table.CodeMap.g.cs", GenerateTableCodeMap(info));
        });

        // ─── [ImTiredOfCrud] meta-attribute pipeline — implies [UiForm] + [UiTable] ──
        var modelFormTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ImTiredOfCrudAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    // Only generate form if explicit [UiForm] is not present
                    var hasForm = ((INamedTypeSymbol)ctx.TargetSymbol).GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == UiFormAttributeFqn);
                    return hasForm ? null : ExtractFormInfo(ctx);
                })
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(modelFormTargets, static (spc, info) =>
        {
            spc.AddSource($"{info.HintName}.Form.Model.g.cs", GenerateFormDescriptor(info));
            spc.AddSource($"{info.HintName}.FormJson.Model.g.cs", GenerateFormJson(info));

            if (info.IsPartial)
                spc.AddSource($"{info.HintName}.Ui.Form.Model.CodeMap.g.cs", GenerateFormCodeMap(info));
        });

        var modelTableTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ImTiredOfCrudAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var hasTable = ((INamedTypeSymbol)ctx.TargetSymbol).GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == UiTableAttributeFqn);
                    return hasTable ? null : ExtractTableInfo(ctx);
                })
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(modelTableTargets, static (spc, info) =>
        {
            spc.AddSource($"{info.HintName}.Table.Model.g.cs", GenerateTableDescriptor(info));
            spc.AddSource($"{info.HintName}.TableJson.Model.g.cs", GenerateTableJson(info));

            if (info.IsPartial)
                spc.AddSource($"{info.HintName}.Ui.Table.Model.CodeMap.g.cs", GenerateTableCodeMap(info));
        });

        // ─── Entity pipeline (EF Core — opt-in via [Entity]) ────────────
        var entityTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EntityAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) => ExtractEntityInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(entityTargets, static (spc, info) =>
        {
            spc.AddSource($"{info.HintName}.Entity.g.cs", GenerateEntityConfiguration(info));
        });

        var collectedEntities = entityTargets.Collect();
        context.RegisterSourceOutput(collectedEntities, static (spc, infos) =>
        {
            if (infos.Length > 0)
                spc.AddSource("EntityConfigurations.g.cs", GenerateEntityRegistrations(infos));
        });

        // ─── Schema endpoint helper ─────────────────────────────────────
        // Combines all form/table targets (explicit [UiForm]/[UiTable] AND implicit
        // via [ImTiredOfCrud]) into a single MapZibStackUiSchemas() extension method
        // that registers GET /api/forms/{name} + GET /api/tables/{name} per type.

        var hasAspNetCore = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Results") is not null);

        var allFormTargets = formTargets.Collect()
            .Combine(modelFormTargets.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        var allTableTargets = tableTargets.Collect()
            .Combine(modelTableTargets.Collect())
            .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

        context.RegisterSourceOutput(
            allFormTargets.Combine(allTableTargets).Combine(hasAspNetCore),
            static (spc, pair) =>
            {
                var ((forms, tables), hasAsp) = pair;
                if (!hasAsp) return;
                if (forms.Length == 0 && tables.Length == 0) return;
                spc.AddSource("ZibStackUiSchemaEndpoints.g.cs", GenerateSchemaEndpoints(forms, tables));
            });
    }

    private static string GenerateFormCodeMap(FormClassInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (info.Namespace is not null)
        {
            sb.AppendLine($"namespace {info.Namespace};");
            sb.AppendLine();
        }

        var keyword = info.IsRecord ? "record" : "class";
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Generated code map for <c>{info.ClassName}</c> (UI Form):");
        sb.AppendLine("/// <list type=\"bullet\">");
        sb.AppendLine($"/// <item><description>Form descriptor: <see cref=\"{info.ClassName}FormDescriptor\"/></description></item>");
        sb.AppendLine($"/// <item><description>JSON schema: <see cref=\"{info.ClassName}\"/> — GetFormSchemaJson()</description></item>");
        sb.AppendLine("/// </list>");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"partial {keyword} {info.ClassName} {{ }}");

        return sb.ToString();
    }

    private static string GenerateTableCodeMap(TableClassInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (info.Namespace is not null)
        {
            sb.AppendLine($"namespace {info.Namespace};");
            sb.AppendLine();
        }

        var keyword = info.IsRecord ? "record" : "class";
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Generated code map for <c>{info.ClassName}</c> (UI Table):");
        sb.AppendLine("/// <list type=\"bullet\">");
        sb.AppendLine($"/// <item><description>Table descriptor: <see cref=\"{info.ClassName}TableDescriptor\"/></description></item>");
        sb.AppendLine($"/// <item><description>JSON schema: <see cref=\"{info.ClassName}\"/> — GetTableSchemaJson()</description></item>");
        sb.AppendLine("/// </list>");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"partial {keyword} {info.ClassName} {{ }}");

        return sb.ToString();
    }
}
