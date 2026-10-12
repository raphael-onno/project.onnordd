using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.UnitTests;

public sealed class LegacyTemplateTests : IDisposable
{
    private const string CONTENT_TYPE_VALUES = "Feature;Teaser ;Trailer;";

    private readonly TemplateBuilder _builder = new();

    [Fact]
    public void A_template_column_is_described_by_its_five_header_rows()
    {
        var template = _builder
            .Column("contentType", layer: "Content", requirement: "M", values: CONTENT_TYPE_VALUES, label: "Content Type")
            .Build();

        var column = template.Find("contentType")!;

        Assert.Equal(1, column.Index);
        Assert.Equal("Content", column.Layer);
        Assert.Equal(ColumnRequirement.Mandatory, column.Requirement);
        Assert.Equal(ColumnValueKind.Picklist, column.ValueKind);
        Assert.Equal("Content Type", column.Label);
    }

    [Fact]
    public void A_picklist_keeps_the_exact_spelling_of_its_values()
    {
        var template = _builder.Column("contentType", values: CONTENT_TYPE_VALUES).Build();

        Assert.Equal(["Feature", "Teaser ", "Trailer"], template.Find("contentType")!.AllowedValues);
    }

    [Theory]
    [InlineData("M", ColumnRequirement.Mandatory)]
    [InlineData("O", ColumnRequirement.Optional)]
    [InlineData("A", ColumnRequirement.Automatic)]
    [InlineData("M if Season", ColumnRequirement.MandatoryIfSeason)]
    [InlineData("", ColumnRequirement.Optional)]
    public void The_requirement_row_is_read_into_a_requirement(string text, ColumnRequirement expected)
    {
        var template = _builder.Column("someTag", requirement: text).Build();

        Assert.Equal(expected, template.Find("someTag")!.Requirement);
    }

    [Theory]
    [InlineData("Date", ColumnValueKind.Date)]
    [InlineData("bool", ColumnValueKind.Boolean)]
    [InlineData("False;True;", ColumnValueKind.Boolean)]
    [InlineData("List", ColumnValueKind.PlatformList)]
    [InlineData("List;None", ColumnValueKind.PlatformList)]
    [InlineData("string", ColumnValueKind.Text)]
    [InlineData("", ColumnValueKind.Text)]
    public void The_values_row_tells_the_kind_of_value(string text, ColumnValueKind expected)
    {
        var template = _builder.Column("someTag", values: text).Build();

        Assert.Equal(expected, template.Find("someTag")!.ValueKind);
    }

    [Fact]
    public void Blank_columns_keep_their_position_but_are_not_data_columns()
    {
        var template = _builder.Column("first").Spacer().Column("third").Build();

        Assert.Equal(3, template.Columns.Count);
        Assert.Equal(3, template.Find("third")!.Index);
        Assert.Equal(["first", "third"], template.DataColumns.Select(c => c.TagName));
    }

    public void Dispose() => _builder.Dispose();
}
