using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.UnitTests;

public sealed class ItemStatusTests
{
    private const long MEDIASPOT_ASSET_ID = 6394268;

    [Fact]
    public void An_asset_accepted_by_mediaspot_is_done_whatever_its_cells()
    {
        var item = new IngestItem { MediaspotAssetId = MEDIASPOT_ASSET_ID, MediaspotStatus = "Accepted" };

        Assert.Equal(ItemStatus.Done, ItemStatusRules.Evaluate(item, allCellsValid: false));
    }

    [Theory]
    [InlineData("New")]
    [InlineData("ParsedAndProbbed")]
    public void An_asset_being_ingested_by_mediaspot_is_pending(string status)
    {
        var item = new IngestItem { MediaspotAssetId = MEDIASPOT_ASSET_ID, MediaspotStatus = status };

        Assert.Equal(ItemStatus.Pending, ItemStatusRules.Evaluate(item, allCellsValid: true));
    }

    [Fact]
    public void An_item_with_valid_cells_and_a_restored_file_is_ready()
    {
        var item = new IngestItem { Location = FileLocation.Source };

        Assert.Equal(ItemStatus.Ready, ItemStatusRules.Evaluate(item, allCellsValid: true));
    }

    [Fact]
    public void A_ready_item_once_exported_waits_for_the_import()
    {
        var item = new IngestItem { Location = FileLocation.Ingest, ExportedAt = DateTime.Now };

        Assert.Equal(ItemStatus.Exported, ItemStatusRules.Evaluate(item, allCellsValid: true));
    }

    [Theory]
    [InlineData(FileLocation.NotFound, true)]
    [InlineData(FileLocation.Unknown, true)]
    [InlineData(FileLocation.Source, false)]
    public void An_item_without_its_file_or_with_invalid_cells_needs_a_fix(FileLocation location, bool allCellsValid)
    {
        var item = new IngestItem { Location = location };

        Assert.Equal(ItemStatus.FixNeeded, ItemStatusRules.Evaluate(item, allCellsValid));
    }
}
