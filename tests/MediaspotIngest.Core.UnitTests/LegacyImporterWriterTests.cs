using ClosedXML.Excel;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Export;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.UnitTests;

public sealed class LegacyImporterWriterTests : IDisposable
{
    private const string FILE_NAME = "FILM_FTR_HD.mov";
    private const string FRAME_RATE = "24";

    private readonly TemplateBuilder _builder = new();

    [Fact]
    public void Items_are_written_from_row_6_under_their_tag_name_and_as_text()
    {
        var template = _builder.Column("originalFileName").Column("techFrameRate").Build();
        var item = Item();

        var path = LegacyImporterWriter.Write(template, [item], _builder.Directory, "batch");

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(FILE_NAME, sheet.Cell(6, 1).GetString());
        Assert.Equal(XLDataType.Text, sheet.Cell(6, 2).DataType);
        Assert.Equal(FRAME_RATE, sheet.Cell(6, 2).GetString());
    }

    [Fact]
    public void Header_rows_of_the_template_are_kept()
    {
        var template = _builder.Column("originalFileName", requirement: "M", label: "Original File Name").Build();

        var path = LegacyImporterWriter.Write(template, [Item()], _builder.Directory, "batch");

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        Assert.Equal("originalFileName", sheet.Cell(1, 1).GetString());
        Assert.Equal("M", sheet.Cell(3, 1).GetString());
        Assert.Equal("Original File Name", sheet.Cell(5, 1).GetString());
    }

    [Fact]
    public void Empty_cells_spacers_and_importer_columns_are_written_as_skip_markers()
    {
        var template = _builder.Column("originalFileName").Spacer().Column("assetId", requirement: "A").Column("comment").Build();

        var path = LegacyImporterWriter.Write(template, [Item()], _builder.Directory, "batch");

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(LegacyTemplate.Skip, sheet.Cell(6, 2).GetString());
        Assert.Equal(LegacyTemplate.Skip, sheet.Cell(6, 3).GetString());
        Assert.Equal(LegacyTemplate.Skip, sheet.Cell(6, 4).GetString());
    }

    [Fact]
    public void An_existing_export_is_never_overwritten()
    {
        var template = _builder.Column("originalFileName").Build();
        LegacyImporterWriter.Write(template, [Item()], _builder.Directory, "batch");

        Assert.Throws<IOException>(() => LegacyImporterWriter.Write(template, [Item()], _builder.Directory, "batch"));
    }

    [Theory]
    [InlineData("EUROPACORP Batch_001", "EUROPACORP_Batch_001")]
    [InlineData("lot-01_été", "lot-01__t_")]
    public void Export_names_keep_only_what_the_mediaspot_upload_keeps(string name, string expected)
    {
        Assert.Equal(expected, LegacyImporterWriter.SafeFileName(name));
    }

    private static IngestItem Item()
    {
        var item = new IngestItem { Id = "e1", RelativePath = FILE_NAME };
        item.SetCell("originalFileName", FILE_NAME);
        item.SetCell("techFrameRate", FRAME_RATE);
        return item;
    }

    public void Dispose() => _builder.Dispose();
}
