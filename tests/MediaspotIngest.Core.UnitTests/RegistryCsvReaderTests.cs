using MediaspotIngest.Core.Registry;

namespace MediaspotIngest.Core.UnitTests;

public sealed class RegistryCsvReaderTests
{
    private const string HEADER = "elementId;titleKey;relativePath;isFolder;sizeGb;recommendation;decision;language;tapes";

    [Fact]
    public void Elements_are_read_by_column_name()
    {
        var csv = $"{HEADER}\n0ddd1737e4;20-ANS-D-ECART;FTR\\PRHQ\\film.mov;0;127.703;ingest;;FR-XX;AB2593|AC0159\n";

        var element = Assert.Single(RegistryCsvReader.Read(new StringReader(csv)));

        Assert.Equal("0ddd1737e4", element.Id);
        Assert.Equal("20-ANS-D-ECART", element.TitleKey);
        Assert.Equal(@"FTR\PRHQ\film.mov", element.RelativePath);
        Assert.False(element.IsFolder);
        Assert.Equal(127.703, element.SizeGb);
        Assert.Equal("FR-XX", element.Metadata.Language);
        Assert.Equal(["AB2593", "AC0159"], element.Tapes);
    }

    [Fact]
    public void A_quoted_field_may_hold_semicolons_and_quotes()
    {
        var csv = $"{HEADER}\ne1;T;\"BONUS\\Un \"\"duo\"\"; complice.mov\";0;1;ingest;;;\n";

        Assert.Equal("BONUS\\Un \"duo\"; complice.mov", Assert.Single(RegistryCsvReader.Read(new StringReader(csv))).RelativePath);
    }

    [Theory]
    [InlineData("ingest", "", true)]
    [InlineData("lto8", "", false)]
    [InlineData("todo", "", false)]
    [InlineData("lto8", "ingest", true)]
    [InlineData("ingest", "excl", false)]
    [InlineData("ingest", "lto8", false)]
    public void The_client_decision_wins_over_the_registry_recommendation(string recommendation, string decision, bool toIngest)
    {
        var csv = $"{HEADER}\ne1;T;f.mov;0;1;{recommendation};{decision};;\n";

        Assert.Equal(toIngest, Assert.Single(RegistryCsvReader.Read(new StringReader(csv))).IsToIngest);
    }

    [Fact]
    public void An_export_without_element_ids_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => RegistryCsvReader.Read(new StringReader("titleKey;relativePath\nT;f.mov\n")));
    }
}
