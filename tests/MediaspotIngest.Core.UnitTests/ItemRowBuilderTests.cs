using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Filling;
using MediaspotIngest.Core.Registry;

namespace MediaspotIngest.Core.UnitTests;

public sealed class ItemRowBuilderTests : IDisposable
{
    private const string ELEMENT_ID = "0ddd1737e4";
    private const string MASTER_PATH = @"FTR\PRHQ\20-ANS-D-ECART_FTR_HD-177_24FPS_FR-XX_PRHQ.mov";
    private const string MASTER_FILE_NAME = "20-ANS-D-ECART_FTR_HD-177_24FPS_FR-XX_PRHQ.mov";
    private const string TITLE_CUSTOMER_ID = "EC0001";

    private readonly TemplateBuilder _builder = new();

    [Fact]
    public void A_master_gets_its_file_names_stream_id_and_technical_columns_from_the_registry()
    {
        var template = _builder
            .Column("originalFileName").Column("customerOriginalFileName").Column("streamId")
            .Column("assetType").Column("contentType", "Content").Column("techWrapper", values: "MOV;MXF;")
            .Column("techCodec", values: "ProRes;XDCAM;").Column("techFrameRate", values: "24;25;")
            .Column("languageAudio", values: "List").Column("videoProfile", values: "HD;SD;").Column("techColorSpace")
            .Build();
        var item = Master();

        new ItemRowBuilder(template, new PlatformProfile()).Fill(item, title: null);

        Assert.Equal(MASTER_FILE_NAME, item.GetCell("originalFileName"));
        Assert.Equal(MASTER_FILE_NAME, item.GetCell("customerOriginalFileName"));
        Assert.Equal(ELEMENT_ID, item.GetCell("streamId"));
        Assert.Equal("Video", item.GetCell("assetType"));
        Assert.Equal("Feature", item.GetCell("contentType"));
        Assert.Equal("MOV", item.GetCell("techWrapper"));
        Assert.Equal("ProRes", item.GetCell("techCodec"));
        Assert.Equal("24", item.GetCell("techFrameRate"));
        Assert.Equal("FRA", item.GetCell("languageAudio"));
        Assert.Equal("HD", item.GetCell("videoProfile"));
        Assert.Equal("REC709", item.GetCell("techColorSpace"));
    }

    [Fact]
    public void Title_columns_come_from_the_title_and_fall_back_on_the_profile_defaults()
    {
        var template = _builder
            .Column("titleCustomerId", "Title").Column("primaryTitle", "Title").Column("productionDate", "Title")
            .Column("titleOriginalLanguage", "Title").Column("titleOriginalPictureFormat", "Title").Column("titleType", "Title")
            .Build();
        var title = new TitleInfo { TitleCustomId = TITLE_CUSTOMER_ID, MainTitle = "20 ANS D'ECART", ProductionYear = 2013 };
        var profile = new PlatformProfile { DefaultOriginalLanguage = "FRA", DefaultPictureFormat = "2.39" };
        var item = Master();

        new ItemRowBuilder(template, profile).Fill(item, title);

        Assert.Equal(TITLE_CUSTOMER_ID, item.GetCell("titleCustomerId"));
        Assert.Equal("20 ANS D'ECART", item.GetCell("primaryTitle"));
        Assert.Equal("2013", item.GetCell("productionDate"));
        Assert.Equal("FRA", item.GetCell("titleOriginalLanguage"));
        Assert.Equal("2.39", item.GetCell("titleOriginalPictureFormat"));
        Assert.Equal("Movie", item.GetCell("titleType"));
    }

    [Fact]
    public void The_mediaspot_id_of_a_title_created_from_the_tool_fills_the_importer_title_id_column()
    {
        var template = _builder.Column("titleId", "Title", "A").Build();
        var title = new TitleInfo { TitleCustomId = TITLE_CUSTOMER_ID, MainTitle = "X", VdmTitleId = 6001 };
        var item = Master();

        new ItemRowBuilder(template, new PlatformProfile()).Fill(item, title);

        Assert.Equal("6001", item.GetCell("titleId"));
    }

    [Fact]
    public void A_value_is_written_with_the_picklist_spelling()
    {
        var template = _builder.Column("assetClass", values: "DCP;MEZZ;RAW;").Build();
        var profile = new PlatformProfile { DefaultCellValues = new() { ["assetClass"] = "mezz" } };
        var item = Master();

        new ItemRowBuilder(template, profile).Fill(item, title: null);

        Assert.Equal("MEZZ", item.GetCell("assetClass"));
    }

    [Fact]
    public void A_value_guessed_from_the_file_name_outside_the_picklist_is_left_out()
    {
        var template = _builder.Column("techWrapper", values: "MOV;MXF;").Build();
        var item = IngestItem.FromRegistry(new RegistryElement { Id = "e1", RelativePath = @"SUBTITLE\film.sst" }, "EUROPACORP", 1);

        new ItemRowBuilder(template, new PlatformProfile()).Fill(item, title: null);

        Assert.Null(item.GetCell("techWrapper"));
    }

    [Fact]
    public void A_profile_default_outside_the_picklist_is_kept_for_validation_to_report()
    {
        var template = _builder.Column("assetClass", values: "DCP;MEZZ;RAW;").Build();
        var profile = new PlatformProfile { DefaultCellValues = new() { ["assetClass"] = "MASTER" } };
        var item = Master();

        new ItemRowBuilder(template, profile).Fill(item, title: null);

        Assert.Equal("MASTER", item.GetCell("assetClass"));
    }

    [Fact]
    public void Columns_absent_from_the_template_and_importer_columns_are_never_written()
    {
        var template = _builder.Column("assetId", requirement: "A").Column("assetType").Build();
        var item = Master();

        new ItemRowBuilder(template, new PlatformProfile()).Fill(item, title: null);

        Assert.Null(item.GetCell("assetId"));
        Assert.Null(item.GetCell("originalFileName"));
        Assert.Equal("Video", item.GetCell("assetType"));
    }

    private static IngestItem Master() => IngestItem.FromRegistry(
        new RegistryElement
        {
            Id = ELEMENT_ID,
            TitleKey = "20-ANS-D-ECART",
            RelativePath = MASTER_PATH,
            Metadata = new RegistryMetadata
            {
                Type = "FTR", Codec = "PRHQ", Language = "FR-XX", Ratio = "1.77", FrameRate = "24 fps", Definition = "HD",
            },
        },
        profileName: "EUROPACORP",
        batchNumber: 1);

    public void Dispose() => _builder.Dispose();
}
