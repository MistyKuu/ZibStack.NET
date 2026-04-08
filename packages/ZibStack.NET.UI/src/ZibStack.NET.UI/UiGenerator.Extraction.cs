using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZibStack.NET.UI;

public partial class UiGenerator
{
    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string HumanizePropertyName(string name)
    {
        // "FirstName" -> "First Name", "createdAt" -> "Created At"
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (i + 1 >= name.Length || !char.IsUpper(name[i - 1])))
            {
                sb.Append(' ');
            }
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }

    private static string ResolveFieldType(ITypeSymbol type)
    {
        var displayName = type.ToDisplayString();

        // Unwrap nullable
        if (type is INamedTypeSymbol named && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            return ResolveFieldType(named.TypeArguments[0]);

        if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol namedRef)
        {
            if (namedRef.TypeArguments.Length > 0)
                return ResolveFieldType(namedRef.TypeArguments[0]);
            return ResolveFieldTypeFromName(namedRef.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(), type);
        }

        return ResolveFieldTypeFromName(displayName, type);
    }

    private static string ResolveFieldTypeFromName(string typeName, ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return "enum";

        switch (typeName)
        {
            case "string":
                return "string";
            case "int":
            case "long":
            case "short":
            case "byte":
            case "System.Int32":
            case "System.Int64":
            case "System.Int16":
            case "System.Byte":
                return "integer";
            case "decimal":
            case "double":
            case "float":
            case "System.Decimal":
            case "System.Double":
            case "System.Single":
                return "decimal";
            case "bool":
            case "System.Boolean":
                return "boolean";
            case "System.DateTime":
            case "System.DateOnly":
                return "date";
            case "System.DateTimeOffset":
                return "datetime";
            case "System.TimeSpan":
            case "System.TimeOnly":
                return "time";
            case "System.Guid":
                return "string";
            default:
                return "object";
        }
    }

    private static string ResolveDefaultUiHint(string fieldType, ITypeSymbol type)
    {
        switch (fieldType)
        {
            case "string": return "text";
            case "integer": return "number";
            case "decimal": return "number";
            case "boolean": return "checkbox";
            case "date": return "datePicker";
            case "datetime": return "dateTimePicker";
            case "time": return "timePicker";
            case "enum": return "select";
            default: return "text";
        }
    }

    private static List<SelectOptionInfo> ExtractEnumOptions(ITypeSymbol type)
    {
        var options = new List<SelectOptionInfo>();
        var enumType = type;

        // Unwrap nullable
        if (type is INamedTypeSymbol named && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            enumType = named.TypeArguments[0];

        if (enumType.TypeKind != TypeKind.Enum) return options;

        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol field && field.HasConstantValue)
            {
                options.Add(new SelectOptionInfo(field.Name, HumanizePropertyName(field.Name)));
            }
        }

        return options;
    }

    private static bool HasAttribute(IPropertySymbol prop, string fqn)
    {
        return prop.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fqn);
    }

    private static AttributeData? GetAttribute(IPropertySymbol prop, string fqn)
    {
        return prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fqn);
    }

    private static AttributeData? GetAttribute(INamedTypeSymbol type, string fqn)
    {
        return type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fqn);
    }

    private static string? GetNamedArgString(AttributeData attr, string key)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == key);
        return arg.Value.Value as string;
    }

    private static int? GetNamedArgInt(AttributeData attr, string key)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == key);
        return arg.Value.Value as int?;
    }

    private static bool GetNamedArgBool(AttributeData attr, string key, bool defaultValue = false)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == key);
        return arg.Value.Value is bool b ? b : defaultValue;
    }

    private static double GetNamedArgDouble(AttributeData attr, string key, double defaultValue = 0)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == key);
        if (arg.Value.Value is double d) return d;
        if (arg.Value.Value is int i) return i;
        if (arg.Value.Value is float f) return f;
        return defaultValue;
    }

    // ─── Form extraction ─────────────────────────────────────────────────

    private static FormClassInfo? ExtractFormInfo(GeneratorAttributeSyntaxContext context)
    {
        try
        {
            var symbol = (INamedTypeSymbol)context.TargetSymbol;
            var ns = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            var isRecord = context.TargetNode is Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax;
            var hintName = symbol.ToDisplayString().Replace(".", "_").Replace("<", "_").Replace(">", "_");

            // Read [Form] or [Model] attribute
            var formAttr = GetAttribute(symbol, FormAttributeFqn) ?? GetAttribute(symbol, ModelAttributeFqn);
            var formName = formAttr is not null ? GetNamedArgString(formAttr, "Name") ?? symbol.Name : symbol.Name;
            var layout = formAttr is not null ? GetNamedArgString(formAttr, "Layout") ?? "vertical" : "vertical";

            // Read [FormGroup] attributes
            var groups = new List<FormGroupInfo>();
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == FormGroupAttributeFqn)
                {
                    var name = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
                    if (name != null)
                    {
                        var label = GetNamedArgString(attr, "Label");
                        var order = GetNamedArgInt(attr, "Order") ?? 0;
                        groups.Add(new FormGroupInfo(name, label, order));
                    }
                }
            }

            // Read properties
            var fields = new List<FormFieldInfo>();
            int autoOrder = 0;

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetMethod is null) continue;

                // Check [FormIgnore]
                if (HasAttribute(prop, FormIgnoreAttributeFqn)) continue;
                if (HasAttribute(prop, OneToManyAttributeFqn) || HasAttribute(prop, OneToOneAttributeFqn)) continue;

                var fieldType = ResolveFieldType(prop.Type);
                var uiHint = ResolveDefaultUiHint(fieldType, prop.Type);
                var jsonName = ToCamelCase(prop.Name);
                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                    || (prop.Type is INamedTypeSymbol nt && nt.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T);

                var field = new FormFieldInfo(prop.Name, jsonName, prop.Type.ToDisplayString(), fieldType, uiHint)
                {
                    Label = HumanizePropertyName(prop.Name),
                    Order = autoOrder++,
                    IsNullable = isNullable,
                    IsRequired = prop.IsRequired,
                };

                // Enum options
                if (fieldType == "enum")
                {
                    field.IsEnum = true;
                    field.Options.AddRange(ExtractEnumOptions(prop.Type));
                }

                // [FormField]
                var formFieldAttr = GetAttribute(prop, FormFieldAttributeFqn);
                if (formFieldAttr != null)
                {
                    var label = GetNamedArgString(formFieldAttr, "Label");
                    if (label != null) field.Label = label;
                    field.Placeholder = GetNamedArgString(formFieldAttr, "Placeholder");
                    field.HelpText = GetNamedArgString(formFieldAttr, "HelpText");
                    var order = GetNamedArgInt(formFieldAttr, "Order");
                    if (order.HasValue && order.Value >= 0) field.Order = order.Value;
                    field.Group = GetNamedArgString(formFieldAttr, "Group");
                }

                // [FormOrder]
                var formOrderAttr = GetAttribute(prop, FormOrderAttributeFqn);
                if (formOrderAttr != null && formOrderAttr.ConstructorArguments.Length > 0)
                {
                    if (formOrderAttr.ConstructorArguments[0].Value is int o)
                        field.Order = o;
                }

                // [FormSection]
                var formSectionAttr = GetAttribute(prop, FormSectionAttributeFqn);
                if (formSectionAttr != null && formSectionAttr.ConstructorArguments.Length > 0)
                {
                    if (formSectionAttr.ConstructorArguments[0].Value is string grp)
                        field.Group = grp;
                }

                // [FormHidden]
                if (HasAttribute(prop, FormHiddenAttributeFqn))
                    field.IsHidden = true;

                // [FormReadOnly]
                if (HasAttribute(prop, FormReadOnlyAttributeFqn))
                    field.IsReadOnly = true;

                // [FormDisabled]
                if (HasAttribute(prop, FormDisabledAttributeFqn))
                    field.IsDisabled = true;

                // UI hint attributes
                ExtractUiHints(prop, field);

                // [FormConditional]
                var condAttr = GetAttribute(prop, FormConditionalAttributeFqn);
                if (condAttr != null && condAttr.ConstructorArguments.Length >= 2)
                {
                    var condField = condAttr.ConstructorArguments[0].Value as string;
                    var condValue = condAttr.ConstructorArguments[1].Value as string;
                    var condOp = GetNamedArgString(condAttr, "Operator") ?? "equals";
                    if (condField != null && condValue != null)
                        field.Conditional = new ConditionalInfo(ToCamelCase(condField), condValue, condOp.ToLowerInvariant());
                }

                // Cross-package: Validation attributes
                ExtractValidationAttributes(prop, field);

                // Cross-package: Dto attributes
                ExtractDtoAttributes(prop, field);

                fields.Add(field);
            }

            var formRelations = new List<RelationInfo>();
            ExtractRelationsFromProperties(symbol, formRelations);

            var formInfo = new FormClassInfo(symbol.Name, ns, hintName, formName, layout.ToLowerInvariant(), isRecord, fields, groups);
            formInfo.Relations.AddRange(formRelations);
            return formInfo;
        }
        catch
        {
            return null;
        }
    }

    private static void ExtractUiHints(IPropertySymbol prop, FormFieldInfo field)
    {
        // [TextArea]
        var textArea = GetAttribute(prop, TextAreaAttributeFqn);
        if (textArea != null)
        {
            field.UiHint = "textarea";
            var rows = GetNamedArgInt(textArea, "Rows") ?? 3;
            field.Props["rows"] = rows.ToString();
        }

        // [Select]
        var selectAttr = GetAttribute(prop, SelectAttributeFqn);
        if (selectAttr != null)
        {
            field.UiHint = "select";
            ExtractSelectOptions(selectAttr, field);
        }

        // [RadioGroup]
        var radioAttr = GetAttribute(prop, RadioGroupAttributeFqn);
        if (radioAttr != null)
        {
            field.UiHint = "radioGroup";
            ExtractSelectOptions(radioAttr, field);
        }

        // [Checkbox]
        if (HasAttribute(prop, CheckboxAttributeFqn))
            field.UiHint = "checkbox";

        // [DatePicker]
        var dateAttr = GetAttribute(prop, DatePickerAttributeFqn);
        if (dateAttr != null)
        {
            field.UiHint = "datePicker";
            var min = GetNamedArgString(dateAttr, "Min");
            var max = GetNamedArgString(dateAttr, "Max");
            if (min != null) field.Props["min"] = min;
            if (max != null) field.Props["max"] = max;
        }

        // [TimePicker]
        if (HasAttribute(prop, TimePickerAttributeFqn))
            field.UiHint = "timePicker";

        // [DateTimePicker]
        if (HasAttribute(prop, DateTimePickerAttributeFqn))
            field.UiHint = "dateTimePicker";

        // [FilePicker]
        var fileAttr = GetAttribute(prop, FilePickerAttributeFqn);
        if (fileAttr != null)
        {
            field.UiHint = "filePicker";
            var accept = GetNamedArgString(fileAttr, "Accept");
            if (accept != null) field.Props["accept"] = accept;
            if (GetNamedArgBool(fileAttr, "Multiple"))
                field.Props["multiple"] = "true";
        }

        // [ColorPicker]
        if (HasAttribute(prop, ColorPickerAttributeFqn))
            field.UiHint = "colorPicker";

        // [RichText]
        if (HasAttribute(prop, RichTextAttributeFqn))
            field.UiHint = "richText";

        // [Slider]
        var sliderAttr = GetAttribute(prop, SliderAttributeFqn);
        if (sliderAttr != null)
        {
            field.UiHint = "slider";
            field.Props["min"] = GetNamedArgDouble(sliderAttr, "Min", 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            field.Props["max"] = GetNamedArgDouble(sliderAttr, "Max", 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
            field.Props["step"] = GetNamedArgDouble(sliderAttr, "Step", 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // [PasswordInput]
        if (HasAttribute(prop, PasswordInputAttributeFqn))
            field.UiHint = "password";
    }

    private static void ExtractSelectOptions(AttributeData attr, FormFieldInfo field)
    {
        // Check for enum type in constructor arg
        if (attr.ConstructorArguments.Length > 0)
        {
            var arg = attr.ConstructorArguments[0];
            if (arg.Value is INamedTypeSymbol enumType && enumType.TypeKind == TypeKind.Enum)
            {
                field.Options.Clear();
                foreach (var member in enumType.GetMembers())
                {
                    if (member is IFieldSymbol f && f.HasConstantValue)
                        field.Options.Add(new SelectOptionInfo(f.Name, HumanizePropertyName(f.Name)));
                }
                return;
            }

            // Check for string[] options
            if (arg.Kind == TypedConstantKind.Array)
            {
                field.Options.Clear();
                foreach (var item in arg.Values)
                {
                    if (item.Value is string s)
                        field.Options.Add(new SelectOptionInfo(s, s));
                }
            }
        }
    }

    // ─── Cross-package: Validation ───────────────────────────────────────

    private static void ExtractValidationAttributes(IPropertySymbol prop, FormFieldInfo field)
    {
        foreach (var attr in prop.GetAttributes())
        {
            var fqn = attr.AttributeClass?.ToDisplayString();
            if (fqn == null) continue;

            switch (fqn)
            {
                case "ZibStack.NET.Validation.RequiredAttribute":
                case "System.ComponentModel.DataAnnotations.RequiredAttribute":
                    field.IsRequired = true;
                    field.ValidationRules.Add(new ValidationRuleInfo("required"));
                    break;

                case "ZibStack.NET.Validation.MinLengthAttribute":
                case "System.ComponentModel.DataAnnotations.MinLengthAttribute":
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int minLen)
                    {
                        var rule = new ValidationRuleInfo("minLength") { MinValue = minLen };
                        field.ValidationRules.Add(rule);
                    }
                    break;

                case "ZibStack.NET.Validation.MaxLengthAttribute":
                case "System.ComponentModel.DataAnnotations.MaxLengthAttribute":
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int maxLen)
                    {
                        var rule = new ValidationRuleInfo("maxLength") { MaxValue = maxLen };
                        field.ValidationRules.Add(rule);
                    }
                    break;

                case "ZibStack.NET.Validation.RangeAttribute":
                case "System.ComponentModel.DataAnnotations.RangeAttribute":
                    if (attr.ConstructorArguments.Length >= 2)
                    {
                        var min = attr.ConstructorArguments[0].Value is double dmin ? dmin
                            : attr.ConstructorArguments[0].Value is int imin ? (double)imin : 0;
                        var max = attr.ConstructorArguments[1].Value is double dmax ? dmax
                            : attr.ConstructorArguments[1].Value is int imax ? (double)imax : 0;
                        var rule = new ValidationRuleInfo("range") { MinValue = min, MaxValue = max };
                        field.ValidationRules.Add(rule);
                    }
                    break;

                case "ZibStack.NET.Validation.EmailAttribute":
                    field.ValidationRules.Add(new ValidationRuleInfo("email"));
                    break;

                case "ZibStack.NET.Validation.UrlAttribute":
                    field.ValidationRules.Add(new ValidationRuleInfo("url"));
                    break;

                case "ZibStack.NET.Validation.MatchAttribute":
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string pattern)
                    {
                        var rule = new ValidationRuleInfo("pattern") { Pattern = pattern };
                        field.ValidationRules.Add(rule);
                    }
                    break;

                case "ZibStack.NET.Validation.NotEmptyAttribute":
                    field.ValidationRules.Add(new ValidationRuleInfo("notEmpty"));
                    break;

                // EF Core data annotations
                case "System.ComponentModel.DataAnnotations.StringLengthAttribute":
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int strMaxLen)
                    {
                        var rule = new ValidationRuleInfo("maxLength") { MaxValue = strMaxLen };
                        field.ValidationRules.Add(rule);
                    }
                    break;
            }
        }
    }

    // ─── Cross-package: Dto ──────────────────────────────────────────────

    private static void ExtractDtoAttributes(IPropertySymbol prop, FormFieldInfo field)
    {
        foreach (var attr in prop.GetAttributes())
        {
            var fqn = attr.AttributeClass?.ToDisplayString();
            switch (fqn)
            {
                case "ZibStack.NET.Dto.CreateOnlyAttribute":
                    field.IsCreateOnly = true;
                    break;
                case "ZibStack.NET.Dto.UpdateOnlyAttribute":
                    field.IsUpdateOnly = true;
                    break;
            }
        }
    }

    // ─── Table extraction ────────────────────────────────────────────────

    private static TableClassInfo? ExtractTableInfo(GeneratorAttributeSyntaxContext context)
    {
        try
        {
            var symbol = (INamedTypeSymbol)context.TargetSymbol;
            var ns = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            var isRecord = context.TargetNode is Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax;
            var hintName = symbol.ToDisplayString().Replace(".", "_").Replace("<", "_").Replace(">", "_");

            // Read [Table] or [Model] attribute
            var tableAttr = GetAttribute(symbol, TableAttributeFqn) ?? GetAttribute(symbol, ModelAttributeFqn);
            var tableName = tableAttr is not null ? GetNamedArgString(tableAttr, "Name") ?? symbol.Name : symbol.Name;
            var defaultPageSize = tableAttr is not null ? GetNamedArgInt(tableAttr, "DefaultPageSize") ?? 20 : 20;
            var defaultSort = tableAttr is not null ? GetNamedArgString(tableAttr, "DefaultSort") : null;
            var defaultSortDirection = tableAttr is not null ? GetNamedArgString(tableAttr, "DefaultSortDirection") ?? "asc" : "asc";

            // PageSizes
            int[] pageSizes = { 10, 20, 50, 100 };
            var pageSizesArg = tableAttr?.NamedArguments.FirstOrDefault(a => a.Key == "PageSizes");
            if (pageSizesArg is { } psa && psa.Value.Kind == TypedConstantKind.Array)
            {
                var values = psa.Value.Values;
                if (values.Length > 0)
                {
                    pageSizes = values
                        .Where(v => v.Value is int)
                        .Select(v => (int)v.Value!)
                        .ToArray();
                }
            }

            // Read properties
            var columns = new List<TableColumnInfo>();
            int autoOrder = 0;

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetMethod is null) continue;

                // Check [TableIgnore]
                if (HasAttribute(prop, TableIgnoreAttributeFqn)) continue;
                if (HasAttribute(prop, OneToManyAttributeFqn) || HasAttribute(prop, OneToOneAttributeFqn)) continue;

                var fieldType = ResolveFieldType(prop.Type);
                var jsonName = ToCamelCase(prop.Name);

                var col = new TableColumnInfo(prop.Name, jsonName, prop.Type.ToDisplayString(), fieldType)
                {
                    Label = HumanizePropertyName(prop.Name),
                    Order = autoOrder++,
                };

                // Enum options for table
                if (fieldType == "enum")
                {
                    col.IsEnum = true;
                    foreach (var opt in ExtractEnumOptions(prop.Type))
                        col.EnumValues.Add(opt.Value);
                }

                // [TableColumn]
                var colAttr = GetAttribute(prop, TableColumnAttributeFqn);
                if (colAttr != null)
                {
                    var label = GetNamedArgString(colAttr, "Label");
                    if (label != null) col.Label = label;
                    col.Sortable = GetNamedArgBool(colAttr, "Sortable");
                    col.Filterable = GetNamedArgBool(colAttr, "Filterable");
                    col.Format = GetNamedArgString(colAttr, "Format");
                    var order = GetNamedArgInt(colAttr, "Order");
                    if (order.HasValue && order.Value >= 0) col.Order = order.Value;
                    col.IsVisible = GetNamedArgBool(colAttr, "IsVisible", true);
                    col.Width = GetNamedArgString(colAttr, "Width");
                }

                // [Computed]
                if (HasAttribute(prop, ComputedAttributeFqn))
                    col.IsComputed = true;

                // [ColumnStyle] (AllowMultiple)
                foreach (var styleAttr in prop.GetAttributes())
                {
                    if (styleAttr.AttributeClass?.ToDisplayString() == ColumnStyleAttributeFqn)
                    {
                        var when = GetNamedArgString(styleAttr, "When");
                        var severity = GetNamedArgString(styleAttr, "Severity");
                        if (when != null && severity != null)
                            col.Styles.Add(new ColumnStyleInfo(when, severity));
                    }
                }

                columns.Add(col);
            }

            var schemaUrl = tableAttr is not null ? GetNamedArgString(tableAttr, "SchemaUrl") : null;

            var result = new TableClassInfo(symbol.Name, ns, hintName, tableName, isRecord, columns,
                defaultPageSize, pageSizes, defaultSort, defaultSortDirection.ToLowerInvariant(), schemaUrl);

            ExtractRelationsFromProperties(symbol, result.Relations);

            // ERP: class-level attributes
            ExtractErpClassAttributes(symbol, result);

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static void ExtractErpClassAttributes(INamedTypeSymbol symbol, TableClassInfo info)
    {
        var permissions = new PermissionInfo();

        foreach (var attr in symbol.GetAttributes())
        {
            var fqn = attr.AttributeClass?.ToDisplayString();
            if (fqn == null) continue;

            switch (fqn)
            {
                case ChildTableAttributeFqn:
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is INamedTypeSymbol targetType)
                    {
                        var foreignKey = GetNamedArgString(attr, "ForeignKey") ?? "";
                        var label = GetNamedArgString(attr, "Label") ?? targetType.Name;
                        var childSchemaUrl = GetNamedArgString(attr, "SchemaUrl");

                        if (childSchemaUrl == null)
                        {
                            var targetTableAttr = GetAttribute(targetType, TableAttributeFqn);
                            if (targetTableAttr != null)
                                childSchemaUrl = GetNamedArgString(targetTableAttr, "SchemaUrl");
                        }

                        if (childSchemaUrl == null)
                        {
                            var targetName = targetType.Name;
                            if (targetName.EndsWith("View")) targetName = targetName.Substring(0, targetName.Length - 4);
                            childSchemaUrl = "/api/tables/" + targetName.ToLowerInvariant();
                        }
                        info.Relations.Add(new RelationInfo(RelationKind.OneToMany, targetType.Name, ToCamelCase(targetType.Name), ToCamelCase(foreignKey), label, childSchemaUrl));
                    }
                    break;

                case RowActionAttributeFqn:
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string raName)
                    {
                        info.RowActions.Add(new RowActionInfo(
                            raName,
                            GetNamedArgString(attr, "Label") ?? raName,
                            GetNamedArgString(attr, "Icon"),
                            GetNamedArgString(attr, "Endpoint") ?? "",
                            (GetNamedArgString(attr, "Method") ?? "GET").ToUpperInvariant(),
                            GetNamedArgString(attr, "Confirmation"),
                            GetNamedArgString(attr, "Permission")));
                    }
                    break;

                case ToolbarActionAttributeFqn:
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string taName)
                    {
                        info.ToolbarActions.Add(new ToolbarActionInfo(
                            taName,
                            GetNamedArgString(attr, "Label") ?? taName,
                            GetNamedArgString(attr, "Icon"),
                            GetNamedArgString(attr, "Endpoint") ?? "",
                            (GetNamedArgString(attr, "Method") ?? "POST").ToUpperInvariant(),
                            GetNamedArgString(attr, "Confirmation"),
                            GetNamedArgString(attr, "Permission"),
                            (GetNamedArgString(attr, "SelectionMode") ?? "none").ToLowerInvariant()));
                    }
                    break;

                case PermissionAttributeFqn:
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string perm)
                        permissions.ViewPermission = perm;
                    break;

                case ColumnPermissionAttributeFqn:
                    if (attr.ConstructorArguments.Length >= 2
                        && attr.ConstructorArguments[0].Value is string colName
                        && attr.ConstructorArguments[1].Value is string colPerm)
                    {
                        permissions.ColumnPermissions[ToCamelCase(colName)] = colPerm;
                    }
                    break;

                case DataFilterAttributeFqn:
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string filterProp)
                        permissions.DataFilters.Add(ToCamelCase(filterProp));
                    break;
            }
        }

        if (permissions.HasAny)
            info.Permissions = permissions;
    }

    private static void ExtractRelationsFromProperties(INamedTypeSymbol symbol, List<RelationInfo> relations)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;

            var oneToManyAttr = GetAttribute(prop, OneToManyAttributeFqn);
            var oneToOneAttr = GetAttribute(prop, OneToOneAttributeFqn);

            if (oneToManyAttr != null)
            {
                var targetType = ExtractCollectionElementType(prop.Type);
                if (targetType == null) continue;

                var fk = GetNamedArgString(oneToManyAttr, "ForeignKey");
                if (fk == null)
                    fk = AutoDetectForeignKey(symbol, targetType, RelationKind.OneToMany, prop);
                if (fk == null) fk = "";

                var label = GetNamedArgString(oneToManyAttr, "Label") ?? HumanizePropertyName(prop.Name);
                var schemaUrl = ResolveSchemaUrl(oneToManyAttr, targetType);
                var formSchemaUrl = ResolveFormSchemaUrl(oneToManyAttr, targetType);

                relations.Add(new RelationInfo(RelationKind.OneToMany, targetType.Name, ToCamelCase(prop.Name), ToCamelCase(fk), label, schemaUrl, formSchemaUrl));
            }
            else if (oneToOneAttr != null)
            {
                var targetType = prop.Type as INamedTypeSymbol;
                if (targetType == null) continue;
                if (targetType.NullableAnnotation == NullableAnnotation.Annotated && targetType.TypeArguments.Length > 0)
                    targetType = targetType.TypeArguments[0] as INamedTypeSymbol;
                if (targetType == null || targetType.SpecialType != SpecialType.None) continue;

                var fk = GetNamedArgString(oneToOneAttr, "ForeignKey");
                if (fk == null)
                    fk = AutoDetectForeignKey(symbol, targetType, RelationKind.OneToOne, prop);
                if (fk == null) fk = "";

                var label = GetNamedArgString(oneToOneAttr, "Label") ?? HumanizePropertyName(prop.Name);
                var schemaUrl = ResolveSchemaUrl(oneToOneAttr, targetType);
                var formSchemaUrl = ResolveFormSchemaUrl(oneToOneAttr, targetType);

                relations.Add(new RelationInfo(RelationKind.OneToOne, targetType.Name, ToCamelCase(prop.Name), ToCamelCase(fk), label, schemaUrl, formSchemaUrl));
            }
        }
    }

    private static INamedTypeSymbol? ExtractCollectionElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
            return arrayType.ElementType as INamedTypeSymbol;

        if (type is INamedTypeSymbol named)
        {
            foreach (var iface in named.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"
                    && iface.TypeArguments.Length == 1)
                    return iface.TypeArguments[0] as INamedTypeSymbol;
            }
            if (named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"
                && named.TypeArguments.Length == 1)
                return named.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    private static string? AutoDetectForeignKey(INamedTypeSymbol parentSymbol, INamedTypeSymbol targetType, RelationKind kind, IPropertySymbol navProp)
    {
        if (kind == RelationKind.OneToMany)
        {
            var parentName = parentSymbol.Name;
            if (parentName.EndsWith("View")) parentName = parentName.Substring(0, parentName.Length - 4);

            foreach (var m in targetType.GetMembers())
            {
                if (m is IPropertySymbol p && p.Name == parentName + "Id")
                    return p.Name;
            }
            foreach (var m in targetType.GetMembers())
            {
                if (m is IPropertySymbol p && p.Name == parentSymbol.Name + "Id")
                    return p.Name;
            }
        }
        else if (kind == RelationKind.OneToOne)
        {
            var propName = navProp.Name;
            foreach (var m in parentSymbol.GetMembers())
            {
                if (m is IPropertySymbol p && p.Name == propName + "Id")
                    return p.Name;
            }
        }

        return null;
    }

    private static string? ResolveSchemaUrl(AttributeData attr, INamedTypeSymbol targetType)
    {
        var explicit_ = GetNamedArgString(attr, "SchemaUrl");
        if (explicit_ != null) return explicit_;

        var targetTableAttr = GetAttribute(targetType, TableAttributeFqn);
        if (targetTableAttr != null)
        {
            var fromTable = GetNamedArgString(targetTableAttr, "SchemaUrl");
            if (fromTable != null) return fromTable;
        }

        var targetName = targetType.Name;
        if (targetName.EndsWith("View")) targetName = targetName.Substring(0, targetName.Length - 4);
        return "/api/tables/" + targetName.ToLowerInvariant();
    }

    private static string? ResolveFormSchemaUrl(AttributeData attr, INamedTypeSymbol targetType)
    {
        var explicit_ = GetNamedArgString(attr, "FormSchemaUrl");
        if (explicit_ != null) return explicit_;

        var targetFormAttr = GetAttribute(targetType, FormAttributeFqn);
        if (targetFormAttr != null)
        {
            var targetName = targetType.Name;
            if (targetName.EndsWith("View")) targetName = targetName.Substring(0, targetName.Length - 4);
            return "/api/forms/" + targetName.ToLowerInvariant();
        }

        return null;
    }

    // ─── Entity extraction ──────────────────────────────────────────────

    private static EntityClassInfo? ExtractEntityInfo(GeneratorAttributeSyntaxContext context)
    {
        try
        {
            var compilation = context.SemanticModel.Compilation;
            if (compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1") == null)
                return null;

            var symbol = (INamedTypeSymbol)context.TargetSymbol;
            var ns = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            var isRecord = context.TargetNode is Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax;
            var hintName = symbol.ToDisplayString().Replace(".", "_").Replace("<", "_").Replace(">", "_");
            var fqn = symbol.ToDisplayString();

            var entityAttr = GetAttribute(symbol, EntityAttributeFqn)!;
            var tableName = GetNamedArgString(entityAttr, "TableName");
            var schema = GetNamedArgString(entityAttr, "Schema");

            string? pk = null;
            foreach (var member in symbol.GetMembers())
            {
                if (member is IPropertySymbol p && p.DeclaredAccessibility == Accessibility.Public)
                {
                    if (p.Name == "Id" || p.Name == symbol.Name + "Id")
                    {
                        pk = p.Name;
                        break;
                    }
                }
            }

            var computedProps = new List<string>();
            var navigationProps = new List<string>();
            var relations = new List<EntityRelationInfo>();

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;

                if (HasAttribute(prop, ComputedAttributeFqn))
                    computedProps.Add(prop.Name);

                var oneToManyAttr = GetAttribute(prop, OneToManyAttributeFqn);
                var oneToOneAttr = GetAttribute(prop, OneToOneAttributeFqn);

                if (oneToManyAttr != null)
                {
                    var targetType = ExtractCollectionElementType(prop.Type);
                    if (targetType == null) continue;

                    navigationProps.Add(prop.Name);

                    var fk = GetNamedArgString(oneToManyAttr, "ForeignKey");
                    if (fk == null)
                        fk = AutoDetectForeignKey(symbol, targetType, RelationKind.OneToMany, prop);

                    relations.Add(new EntityRelationInfo(
                        RelationKind.OneToMany,
                        targetType.Name,
                        targetType.ToDisplayString(),
                        prop.Name,
                        fk));
                }
                else if (oneToOneAttr != null)
                {
                    var targetType = prop.Type as INamedTypeSymbol;
                    if (targetType == null) continue;
                    if (targetType.NullableAnnotation == NullableAnnotation.Annotated && targetType.TypeArguments.Length > 0)
                        targetType = targetType.TypeArguments[0] as INamedTypeSymbol;
                    if (targetType == null || targetType.SpecialType != SpecialType.None) continue;

                    navigationProps.Add(prop.Name);

                    var fk = GetNamedArgString(oneToOneAttr, "ForeignKey");
                    if (fk == null)
                        fk = AutoDetectForeignKey(symbol, targetType, RelationKind.OneToOne, prop);

                    relations.Add(new EntityRelationInfo(
                        RelationKind.OneToOne,
                        targetType.Name,
                        targetType.ToDisplayString(),
                        prop.Name,
                        fk));
                }
            }

            return new EntityClassInfo(
                symbol.Name, ns, hintName, fqn, isRecord,
                tableName, schema, pk,
                relations, computedProps, navigationProps);
        }
        catch
        {
            return null;
        }
    }
}
