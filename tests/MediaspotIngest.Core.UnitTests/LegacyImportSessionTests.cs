using ClosedXML.Excel;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Mediaspot;

namespace MediaspotIngest.Core.UnitTests;

public sealed class LegacyImportSessionTests : IDisposable
{
    private const long FIRST_ASSET_ID = 8550566;
    private const long SECOND_ASSET_ID = 8550567;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MediaspotIngestTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_request_ids_written_by_the_importer_are_stored_on_their_items()
    {
        var items = new[] { Item("a.mov"), Item("b.mov") };
        var path = ImportedSheet(("a.mov", FIRST_ASSET_ID.ToString()), ("b.mov", SECOND_ASSET_ID.ToString()));

        var applied = LegacyImportSession.ApplyAssetIds(path, items);

        Assert.Equal(2, applied);
        Assert.Equal(FIRST_ASSET_ID, items[0].MediaspotAssetId);
        Assert.Equal(SECOND_ASSET_ID, items[1].MediaspotAssetId);
        Assert.NotNull(items[0].ExportedAt);
    }

    [Fact]
    public void A_row_left_unimported_or_not_matching_its_file_name_is_ignored()
    {
        var items = new[] { Item("a.mov"), Item("b.mov") };
        var path = ImportedSheet(("a.mov", "##"), ("other.mov", SECOND_ASSET_ID.ToString()));

        var applied = LegacyImportSession.ApplyAssetIds(path, items);

        Assert.Equal(0, applied);
        Assert.Null(items[0].MediaspotAssetId);
        Assert.Null(items[1].MediaspotAssetId);
    }

    private static IngestItem Item(string fileName) => new() { Id = fileName, RelativePath = fileName };

    private string ImportedSheet(params (string FileName, string AssetId)[] rows)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "imported.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Feuil1");
        sheet.Cell(1, 1).SetValue("originalFileName");
        sheet.Cell(1, 2).SetValue("assetId");
        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(6 + i, 1).SetValue(rows[i].FileName);
            sheet.Cell(6 + i, 2).SetValue(rows[i].AssetId);
        }

        workbook.SaveAs(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
