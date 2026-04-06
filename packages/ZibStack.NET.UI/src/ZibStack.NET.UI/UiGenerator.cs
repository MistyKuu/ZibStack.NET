using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.UI;

[Generator]
public sealed partial class UiGenerator : IIncrementalGenerator
{
    // ─── FQN constants ───────────────────────────────────────────────────

    // Class-level
    private const string FormAttributeFqn = "ZibStack.NET.UI.FormAttribute";
    private const string FormGroupAttributeFqn = "ZibStack.NET.UI.FormGroupAttribute";
    private const string TableAttributeFqn = "ZibStack.NET.UI.TableAttribute";

    // Property-level — form
    private const string FormFieldAttributeFqn = "ZibStack.NET.UI.FormFieldAttribute";
    private const string FormIgnoreAttributeFqn = "ZibStack.NET.UI.FormIgnoreAttribute";
    private const string FormHiddenAttributeFqn = "ZibStack.NET.UI.FormHiddenAttribute";
    private const string FormOrderAttributeFqn = "ZibStack.NET.UI.FormOrderAttribute";
    private const string FormReadOnlyAttributeFqn = "ZibStack.NET.UI.FormReadOnlyAttribute";
    private const string FormDisabledAttributeFqn = "ZibStack.NET.UI.FormDisabledAttribute";
    private const string FormSectionAttributeFqn = "ZibStack.NET.UI.FormSectionAttribute";
    private const string FormConditionalAttributeFqn = "ZibStack.NET.UI.FormConditionalAttribute";

    // Property-level — table
    private const string TableColumnAttributeFqn = "ZibStack.NET.UI.TableColumnAttribute";
    private const string TableIgnoreAttributeFqn = "ZibStack.NET.UI.TableIgnoreAttribute";

    // ERP: class-level
    private const string ChildTableAttributeFqn = "ZibStack.NET.UI.ChildTableAttribute";
    private const string RowActionAttributeFqn = "ZibStack.NET.UI.RowActionAttribute";
    private const string ToolbarActionAttributeFqn = "ZibStack.NET.UI.ToolbarActionAttribute";
    private const string PermissionAttributeFqn = "ZibStack.NET.UI.PermissionAttribute";
    private const string ColumnPermissionAttributeFqn = "ZibStack.NET.UI.ColumnPermissionAttribute";
    private const string DataFilterAttributeFqn = "ZibStack.NET.UI.DataFilterAttribute";

    // ERP: property-level
    private const string ComputedAttributeFqn = "ZibStack.NET.UI.ComputedAttribute";
    private const string ColumnStyleAttributeFqn = "ZibStack.NET.UI.ColumnStyleAttribute";

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
            ctx.AddSource("FormAttribute.g.cs", FormAttributeSource);
            ctx.AddSource("FormGroupAttribute.g.cs", FormGroupAttributeSource);
            ctx.AddSource("FormFieldAttribute.g.cs", FormFieldAttributeSource);
            ctx.AddSource("FormIgnoreAttribute.g.cs", FormIgnoreAttributeSource);
            ctx.AddSource("FormHiddenAttribute.g.cs", FormHiddenAttributeSource);
            ctx.AddSource("FormOrderAttribute.g.cs", FormOrderAttributeSource);
            ctx.AddSource("FormReadOnlyAttribute.g.cs", FormReadOnlyAttributeSource);
            ctx.AddSource("FormDisabledAttribute.g.cs", FormDisabledAttributeSource);
            ctx.AddSource("FormSectionAttribute.g.cs", FormSectionAttributeSource);
            ctx.AddSource("FormConditionalAttribute.g.cs", FormConditionalAttributeSource);

            // Table attributes
            ctx.AddSource("TableAttribute.g.cs", TableAttributeSource);
            ctx.AddSource("TableColumnAttribute.g.cs", TableColumnAttributeSource);
            ctx.AddSource("TableIgnoreAttribute.g.cs", TableIgnoreAttributeSource);

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
            ctx.AddSource("ChildTableAttribute.g.cs", ChildTableAttributeSource);
            ctx.AddSource("RowActionAttribute.g.cs", RowActionAttributeSource);
            ctx.AddSource("ToolbarActionAttribute.g.cs", ToolbarActionAttributeSource);
            ctx.AddSource("PermissionAttribute.g.cs", PermissionAttributeSource);
            ctx.AddSource("ColumnPermissionAttribute.g.cs", ColumnPermissionAttributeSource);
            ctx.AddSource("DataFilterAttribute.g.cs", DataFilterAttributeSource);
            ctx.AddSource("ComputedAttribute.g.cs", ComputedAttributeSource);
            ctx.AddSource("ColumnStyleAttribute.g.cs", ColumnStyleAttributeSource);

            // Runtime types
            ctx.AddSource("FormDescriptor.g.cs", FormDescriptorSource);
            ctx.AddSource("TableDescriptor.g.cs", TableDescriptorSource);
        });

        // ─── Form pipeline ──────────────────────────────────────────────
        var formTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                FormAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => ExtractFormInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(formTargets, static (spc, info) =>
        {
            spc.AddSource($"{info.HintName}.Form.g.cs", GenerateFormDescriptor(info));
            spc.AddSource($"{info.HintName}.FormJson.g.cs", GenerateFormJson(info));
        });

        // ─── Table pipeline ─────────────────────────────────────────────
        var tableTargets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                TableAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, _) => ExtractTableInfo(ctx))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(tableTargets, static (spc, info) =>
        {
            spc.AddSource($"{info.HintName}.Table.g.cs", GenerateTableDescriptor(info));
            spc.AddSource($"{info.HintName}.TableJson.g.cs", GenerateTableJson(info));
        });
    }
}
