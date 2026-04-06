using Xunit;
using ZibStack.NET.UI;

namespace ZibStack.NET.UI.Tests;

public class FormTests
{
    [Fact]
    public void Player_ImplementsIFormDescriptorProvider()
    {
        Assert.True(typeof(IFormDescriptorProvider).IsAssignableFrom(typeof(Player)));
    }

    [Fact]
    public void Player_GetFormDescriptor_ReturnsDescriptor()
    {
        var descriptor = Player.GetFormDescriptor();
        Assert.NotNull(descriptor);
        Assert.Equal("Player", descriptor.Name);
        Assert.Equal("vertical", descriptor.Layout);
    }

    [Fact]
    public void Player_FormDescriptor_HasGroups()
    {
        var descriptor = Player.GetFormDescriptor();
        Assert.Equal(2, descriptor.Groups.Count);
        Assert.Equal("basic", descriptor.Groups[0].Name);
        Assert.Equal("Basic Info", descriptor.Groups[0].Label);
        Assert.Equal(1, descriptor.Groups[0].Order);
        Assert.Equal("contact", descriptor.Groups[1].Name);
    }

    [Fact]
    public void Player_FormDescriptor_ExcludesIgnoredFields()
    {
        var descriptor = Player.GetFormDescriptor();
        Assert.DoesNotContain(descriptor.Fields, f => f.Name == "id");
    }

    [Fact]
    public void Player_FormDescriptor_NameField_HasCorrectMetadata()
    {
        var descriptor = Player.GetFormDescriptor();
        var nameField = descriptor.Fields.First(f => f.Name == "name");

        Assert.Equal("string", nameField.Type);
        Assert.Equal("text", nameField.UiHint);
        Assert.Equal("Player Name", nameField.Label);
        Assert.Equal("Enter name...", nameField.Placeholder);
        Assert.Equal("basic", nameField.Group);
    }

    [Fact]
    public void Player_FormDescriptor_LevelField_IsSlider()
    {
        var descriptor = Player.GetFormDescriptor();
        var levelField = descriptor.Fields.First(f => f.Name == "level");

        Assert.Equal("integer", levelField.Type);
        Assert.Equal("slider", levelField.UiHint);
        Assert.Equal("basic", levelField.Group);
        Assert.NotNull(levelField.Props);
        Assert.Equal("1", levelField.Props!["min"]);
        Assert.Equal("100", levelField.Props["max"]);
    }

    [Fact]
    public void Player_FormDescriptor_RoleField_IsSelectWithOptions()
    {
        var descriptor = Player.GetFormDescriptor();
        var roleField = descriptor.Fields.First(f => f.Name == "role");

        Assert.Equal("enum", roleField.Type);
        Assert.Equal("select", roleField.UiHint);
        Assert.NotNull(roleField.Options);
        Assert.Equal(3, roleField.Options!.Count);
        Assert.Equal("Player", roleField.Options[0].Value);
        Assert.Equal("Admin", roleField.Options[2].Value);
    }

    [Fact]
    public void Player_FormDescriptor_BiographyField_IsTextArea()
    {
        var descriptor = Player.GetFormDescriptor();
        var bioField = descriptor.Fields.First(f => f.Name == "biography");

        Assert.Equal("textarea", bioField.UiHint);
        Assert.Equal("Tell us about yourself", bioField.HelpText);
        Assert.NotNull(bioField.Props);
        Assert.Equal("5", bioField.Props!["rows"]);
    }

    [Fact]
    public void Player_FormDescriptor_PasswordField_IsPassword()
    {
        var descriptor = Player.GetFormDescriptor();
        var pwField = descriptor.Fields.First(f => f.Name == "password");

        Assert.Equal("password", pwField.UiHint);
    }

    [Fact]
    public void Player_FormDescriptor_AdminNotesField_HasConditional()
    {
        var descriptor = Player.GetFormDescriptor();
        var notesField = descriptor.Fields.First(f => f.Name == "adminNotes");

        Assert.NotNull(notesField.Conditional);
        Assert.Equal("role", notesField.Conditional!.Field);
        Assert.Equal("equals", notesField.Conditional.Operator);
        Assert.Equal("Admin", notesField.Conditional.Value);
    }

    [Fact]
    public void Player_FormDescriptor_CreatedAtField_IsDatePicker()
    {
        var descriptor = Player.GetFormDescriptor();
        var dateField = descriptor.Fields.First(f => f.Name == "createdAt");

        Assert.Equal("date", dateField.Type);
        Assert.Equal("datePicker", dateField.UiHint);
    }

    [Fact]
    public void SimpleModel_GetFormDescriptor_HasCustomName()
    {
        var descriptor = SimpleModel.GetFormDescriptor();
        Assert.Equal("SimpleForm", descriptor.Name);
    }

    [Fact]
    public void SimpleModel_GetFormDescriptor_AutoDetectsTypes()
    {
        var descriptor = SimpleModel.GetFormDescriptor();

        var firstName = descriptor.Fields.First(f => f.Name == "firstName");
        Assert.Equal("string", firstName.Type);
        Assert.Equal("text", firstName.UiHint);
        Assert.Equal("First Name", firstName.Label);

        var age = descriptor.Fields.First(f => f.Name == "age");
        Assert.Equal("integer", age.Type);
        Assert.Equal("number", age.UiHint);

        var isActive = descriptor.Fields.First(f => f.Name == "isActive");
        Assert.Equal("boolean", isActive.Type);
        Assert.Equal("checkbox", isActive.UiHint);
    }

    [Fact]
    public void FormWithRadioAndFile_RadioGroupField()
    {
        var descriptor = FormWithRadioAndFile.GetFormDescriptor();
        var level = descriptor.Fields.First(f => f.Name == "level");

        Assert.Equal("radioGroup", level.UiHint);
        Assert.NotNull(level.Options);
        Assert.Equal(4, level.Options!.Count);
        Assert.Equal("Easy", level.Options[0].Value);
    }

    [Fact]
    public void FormWithRadioAndFile_FilePickerField()
    {
        var descriptor = FormWithRadioAndFile.GetFormDescriptor();
        var avatar = descriptor.Fields.First(f => f.Name == "avatar");

        Assert.Equal("filePicker", avatar.UiHint);
        Assert.NotNull(avatar.Props);
        Assert.Equal("image/*", avatar.Props!["accept"]);
        Assert.Equal("true", avatar.Props["multiple"]);
    }

    [Fact]
    public void FormWithRadioAndFile_ColorPickerField()
    {
        var descriptor = FormWithRadioAndFile.GetFormDescriptor();
        var color = descriptor.Fields.First(f => f.Name == "favoriteColor");
        Assert.Equal("colorPicker", color.UiHint);
    }

    [Fact]
    public void FormWithRadioAndFile_RichTextField()
    {
        var descriptor = FormWithRadioAndFile.GetFormDescriptor();
        var desc = descriptor.Fields.First(f => f.Name == "description");
        Assert.Equal("richText", desc.UiHint);
    }

    [Fact]
    public void FormWithRadioAndFile_HiddenField()
    {
        var descriptor = FormWithRadioAndFile.GetFormDescriptor();
        var token = descriptor.Fields.First(f => f.Name == "internalToken");
        Assert.True(token.IsHidden);
    }

    [Fact]
    public void FormWithRadioAndFile_ReadOnlyField()
    {
        var descriptor = FormWithRadioAndFile.GetFormDescriptor();
        var field = descriptor.Fields.First(f => f.Name == "createdBy");
        Assert.True(field.IsReadOnly);
    }

    [Fact]
    public void FormWithRadioAndFile_DisabledField()
    {
        var descriptor = FormWithRadioAndFile.GetFormDescriptor();
        var field = descriptor.Fields.First(f => f.Name == "lockedField");
        Assert.True(field.IsDisabled);
    }
}

public class TableTests
{
    [Fact]
    public void Player_ImplementsITableDescriptorProvider()
    {
        Assert.True(typeof(ITableDescriptorProvider).IsAssignableFrom(typeof(Player)));
    }

    [Fact]
    public void Player_GetTableDescriptor_ReturnsDescriptor()
    {
        var descriptor = Player.GetTableDescriptor();
        Assert.NotNull(descriptor);
        Assert.Equal("Player", descriptor.Name);
    }

    [Fact]
    public void Player_TableDescriptor_HasPagination()
    {
        var descriptor = Player.GetTableDescriptor();
        Assert.Equal(25, descriptor.Pagination.DefaultPageSize);
    }

    [Fact]
    public void Player_TableDescriptor_HasDefaultSort()
    {
        var descriptor = Player.GetTableDescriptor();
        Assert.NotNull(descriptor.DefaultSort);
        Assert.Equal("name", descriptor.DefaultSort!.Column);
        Assert.Equal("asc", descriptor.DefaultSort.Direction);
    }

    [Fact]
    public void Player_TableDescriptor_ExcludesIgnoredColumns()
    {
        var descriptor = Player.GetTableDescriptor();
        Assert.DoesNotContain(descriptor.Columns, c => c.Name == "biography");
        Assert.DoesNotContain(descriptor.Columns, c => c.Name == "password");
        Assert.DoesNotContain(descriptor.Columns, c => c.Name == "adminNotes");
    }

    [Fact]
    public void Player_TableDescriptor_NameColumn_IsSortableFilterable()
    {
        var descriptor = Player.GetTableDescriptor();
        var nameCol = descriptor.Columns.First(c => c.Name == "name");

        Assert.True(nameCol.Sortable);
        Assert.True(nameCol.Filterable);
    }

    [Fact]
    public void Player_TableDescriptor_CreatedAtColumn_HasFormat()
    {
        var descriptor = Player.GetTableDescriptor();
        var dateCol = descriptor.Columns.First(c => c.Name == "createdAt");

        Assert.True(dateCol.Sortable);
        Assert.Equal("yyyy-MM-dd", dateCol.Format);
    }

    [Fact]
    public void Player_TableDescriptor_IdColumn_IsHidden()
    {
        var descriptor = Player.GetTableDescriptor();
        var idCol = descriptor.Columns.First(c => c.Name == "id");
        Assert.False(idCol.IsVisible);
    }

    [Fact]
    public void Article_GetTableDescriptor_HasCorrectColumns()
    {
        var descriptor = Article.GetTableDescriptor();
        Assert.Equal("Article", descriptor.Name);
        Assert.Equal(10, descriptor.Pagination.DefaultPageSize);
        Assert.Equal(4, descriptor.Columns.Count);
        Assert.DoesNotContain(descriptor.Columns, c => c.Name == "content");
    }

    [Fact]
    public void Article_TableDescriptor_DifficultyColumn_HasEnumOptions()
    {
        var descriptor = Article.GetTableDescriptor();
        var diffCol = descriptor.Columns.First(c => c.Name == "difficulty");

        Assert.Equal("enum", diffCol.Type);
        Assert.NotNull(diffCol.Options);
        Assert.Equal(4, diffCol.Options!.Count);
        Assert.Contains("Easy", diffCol.Options);
        Assert.Contains("Expert", diffCol.Options);
    }
}

public class JsonSchemaTests
{
    [Fact]
    public void Player_GetFormSchemaJson_ReturnsValidJson()
    {
        var json = Player.GetFormSchemaJson();
        Assert.NotNull(json);
        Assert.Contains("\"name\":\"Player\"", json);
        Assert.Contains("\"layout\":\"vertical\"", json);
        Assert.Contains("\"fields\":[", json);
        Assert.Contains("\"groups\":[", json);
    }

    [Fact]
    public void Player_GetTableSchemaJson_ReturnsValidJson()
    {
        var json = Player.GetTableSchemaJson();
        Assert.NotNull(json);
        Assert.Contains("\"name\":\"Player\"", json);
        Assert.Contains("\"columns\":[", json);
        Assert.Contains("\"pagination\":", json);
        Assert.Contains("\"defaultPageSize\":25", json);
    }

    [Fact]
    public void Player_FormJson_ContainsSliderProps()
    {
        var json = Player.GetFormSchemaJson();
        Assert.Contains("\"uiHint\":\"slider\"", json);
        Assert.Contains("\"props\":", json);
    }

    [Fact]
    public void Player_FormJson_ContainsConditional()
    {
        var json = Player.GetFormSchemaJson();
        Assert.Contains("\"conditional\":", json);
        Assert.Contains("\"operator\":\"equals\"", json);
    }
}

public class ErpTests
{
    [Fact]
    public void Voivodeship_HasChildTables()
    {
        var descriptor = VoivodeshipView.GetTableDescriptor();
        Assert.Equal(2, descriptor.Children.Count);
        Assert.Equal("Powiaty", descriptor.Children[0].Label);
        Assert.Equal("CountyView", descriptor.Children[0].Target);
        Assert.Equal("voivodeshipId", descriptor.Children[0].ForeignKey);
        // CountyView has no [Table], so convention fallback applies
        Assert.Equal("/api/tables/county", descriptor.Children[0].SchemaUrl);
        Assert.Equal("Kody pocztowe", descriptor.Children[1].Label);
        // PostalCodeView has [Table(SchemaUrl = "/custom/postalcodes")], resolved from target type
        Assert.Equal("/custom/postalcodes", descriptor.Children[1].SchemaUrl);
    }

    [Fact]
    public void Voivodeship_HasRowActions()
    {
        var descriptor = VoivodeshipView.GetTableDescriptor();
        Assert.Equal(2, descriptor.RowActions.Count);

        var details = descriptor.RowActions[0];
        Assert.Equal("showDetails", details.Name);
        Assert.Equal("Szczegóły", details.Label);
        Assert.Equal("/api/voivodeships/{id}", details.Endpoint);
        Assert.Equal("GET", details.Method);

        var report = descriptor.RowActions[1];
        Assert.Equal("generateReport", report.Name);
        Assert.Equal("file", report.Icon);
        Assert.Equal("POST", report.Method);
        Assert.Equal("Wygenerować raport?", report.Confirmation);
    }

    [Fact]
    public void Voivodeship_HasToolbarActions()
    {
        var descriptor = VoivodeshipView.GetTableDescriptor();
        Assert.Equal(2, descriptor.ToolbarActions.Count);

        var export = descriptor.ToolbarActions[0];
        Assert.Equal("export", export.Name);
        Assert.Equal("multiple", export.SelectionMode);
        Assert.Equal("download", export.Icon);

        var recalc = descriptor.ToolbarActions[1];
        Assert.Equal("recalculate", recalc.Name);
        Assert.Equal("Przeliczyć salda?", recalc.Confirmation);
        Assert.Equal("finance.write", recalc.Permission);
    }

    [Fact]
    public void Voivodeship_HasPermissions()
    {
        var descriptor = VoivodeshipView.GetTableDescriptor();
        Assert.NotNull(descriptor.Permissions);
        Assert.Equal("voivodeship.read", descriptor.Permissions!.View);
        Assert.Equal("finance.read", descriptor.Permissions.Columns!["budget"]);
        Assert.Contains("voivodeshipId", descriptor.Permissions.DataFilters!);
    }

    [Fact]
    public void Voivodeship_BudgetColumn_IsComputed()
    {
        var descriptor = VoivodeshipView.GetTableDescriptor();
        var budget = descriptor.Columns.First(c => c.Name == "budget");
        Assert.True(budget.IsComputed);
    }

    [Fact]
    public void Voivodeship_BudgetColumn_HasStyles()
    {
        var descriptor = VoivodeshipView.GetTableDescriptor();
        var budget = descriptor.Columns.First(c => c.Name == "budget");
        Assert.NotNull(budget.Styles);
        Assert.Equal(2, budget.Styles!.Count);
        Assert.Equal("value < 0", budget.Styles[0].When);
        Assert.Equal("danger", budget.Styles[0].Severity);
        Assert.Equal("value >= 0", budget.Styles[1].When);
        Assert.Equal("success", budget.Styles[1].Severity);
    }

    [Fact]
    public void Voivodeship_CountyCountColumn_IsComputed()
    {
        var descriptor = VoivodeshipView.GetTableDescriptor();
        var col = descriptor.Columns.First(c => c.Name == "countyCount");
        Assert.True(col.IsComputed);
    }

    [Fact]
    public void Voivodeship_Json_ContainsChildren()
    {
        var json = VoivodeshipView.GetTableSchemaJson();
        Assert.Contains("\"children\":[", json);
        Assert.Contains("\"target\":\"CountyView\"", json);
        Assert.Contains("\"foreignKey\":\"voivodeshipId\"", json);
        Assert.Contains("\"schemaUrl\":\"/api/tables/county\"", json);
        Assert.Contains("\"schemaUrl\":\"/custom/postalcodes\"", json);
    }

    [Fact]
    public void PostalCodeView_Json_ContainsOwnSchemaUrl()
    {
        var json = PostalCodeView.GetTableSchemaJson();
        Assert.Contains("\"schemaUrl\":\"/custom/postalcodes\"", json);
    }

    [Fact]
    public void Voivodeship_Json_ContainsRowActions()
    {
        var json = VoivodeshipView.GetTableSchemaJson();
        Assert.Contains("\"rowActions\":[", json);
        Assert.Contains("\"endpoint\":\"/api/voivodeships/{id}\"", json);
    }

    [Fact]
    public void Voivodeship_Json_ContainsToolbarActions()
    {
        var json = VoivodeshipView.GetTableSchemaJson();
        Assert.Contains("\"toolbarActions\":[", json);
        Assert.Contains("\"selectionMode\":\"multiple\"", json);
    }

    [Fact]
    public void Voivodeship_Json_ContainsPermissions()
    {
        var json = VoivodeshipView.GetTableSchemaJson();
        Assert.Contains("\"permissions\":{", json);
        Assert.Contains("\"view\":\"voivodeship.read\"", json);
        Assert.Contains("\"finance.read\"", json);
        Assert.Contains("\"dataFilters\":[", json);
    }

    [Fact]
    public void Voivodeship_Json_ContainsComputedAndStyles()
    {
        var json = VoivodeshipView.GetTableSchemaJson();
        Assert.Contains("\"computed\":true", json);
        Assert.Contains("\"styles\":[", json);
        Assert.Contains("\"severity\":\"danger\"", json);
    }
}
