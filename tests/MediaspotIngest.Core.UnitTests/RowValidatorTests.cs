using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Validation;

namespace MediaspotIngest.Core.UnitTests;

public sealed class RowValidatorTests : IDisposable
{
    private const string TITLE_CUSTOMER_ID = "EC0001";

    private readonly TemplateBuilder _builder = new();

    [Fact]
    public void A_complete_row_has_no_issue()
    {
        var validator = Validator();
        var item = ValidItem();

        Assert.Empty(validator.Validate(item));
    }

    [Fact]
    public void A_mandatory_cell_left_empty_or_skipped_is_an_issue()
    {
        var validator = Validator();
        var item = ValidItem();
        item.SetCell("contentType", "##");

        var issue = Assert.Single(validator.Validate(item));
        Assert.Equal("contentType", issue.Column.TagName);
    }

    [Fact]
    public void A_value_outside_the_picklist_is_an_issue()
    {
        var validator = Validator();
        var item = ValidItem();
        item.SetCell("contentType", "Feature Film");

        Assert.Equal("contentType", Assert.Single(validator.Validate(item)).Column.TagName);
    }

    [Fact]
    public void Season_columns_are_mandatory_for_a_season_only()
    {
        var validator = Validator();
        var movie = ValidItem();
        var season = ValidItem();
        season.SetCell("titleType", "Season");

        Assert.Empty(validator.Validate(movie));
        Assert.Equal("serieSeasonNumber", Assert.Single(validator.Validate(season)).Column.TagName);
    }

    [Fact]
    public void Importer_columns_are_never_checked()
    {
        var validator = Validator();
        var item = ValidItem();

        Assert.DoesNotContain(validator.Validate(item), i => i.Column.TagName == "assetId");
    }

    [Fact]
    public void A_row_attached_to_its_title_by_the_mediaspot_id_needs_no_title_id()
    {
        var validator = Validator();
        var item = ValidItem();
        item.SetCell("titleCustomerId", "##");
        item.SetCell("titleId", "6001");

        Assert.Empty(validator.Validate(item));
    }

    [Fact]
    public void A_row_with_neither_title_id_nor_mediaspot_title_id_is_an_issue()
    {
        var validator = Validator();
        var item = ValidItem();
        item.SetCell("titleCustomerId", "##");

        Assert.Equal("titleId", Assert.Single(validator.Validate(item)).Column.TagName);
    }

    [Fact]
    public void A_production_date_must_be_a_year()
    {
        var validator = Validator();
        var item = ValidItem();
        item.SetCell("productionDate", "15/10/2020");

        Assert.Equal("productionDate", Assert.Single(validator.Validate(item)).Column.TagName);
    }

    private RowValidator Validator() => new(_builder
        .Column("titleCustomerId", "Title", "O")
        .Column("titleId", "Title", "A")
        .Column("productionDate", "Title", "M")
        .Column("titleType", "Title", "M", "Movie;Season;")
        .Column("serieSeasonNumber", "Title", "M if Season")
        .Column("contentType", "Content", "M", "Feature;Trailer;")
        .Column("assetId", "Asset", "A")
        .Build());

    private static IngestItem ValidItem()
    {
        var item = new IngestItem { Id = "e1", RelativePath = "film.mov" };
        item.SetCell("titleCustomerId", TITLE_CUSTOMER_ID);
        item.SetCell("productionDate", "2013");
        item.SetCell("titleType", "Movie");
        item.SetCell("contentType", "Feature");
        return item;
    }

    public void Dispose() => _builder.Dispose();
}
