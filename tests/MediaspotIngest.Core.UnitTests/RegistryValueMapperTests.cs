using MediaspotIngest.Core.Filling;

namespace MediaspotIngest.Core.UnitTests;

public sealed class RegistryValueMapperTests
{
    [Theory]
    [InlineData("FR-XX", "FRA", null)]
    [InlineData("FR-EN", "FRA", "ENG")]
    [InlineData("ES-XX", "ESP", null)]
    [InlineData("XX-XX", null, null)]
    [InlineData(null, null, null)]
    public void A_registry_language_gives_the_audio_then_the_subtitle_language(string? registry, string? audio, string? subtitle)
    {
        Assert.Equal((audio, subtitle), RegistryValueMapper.Languages(registry));
    }

    [Theory]
    [InlineData("24 fps", "24")]
    [InlineData("25 fps", "25")]
    [InlineData("23 fps", "23.98")]
    [InlineData("29 fps", "29.97")]
    [InlineData("", null)]
    public void A_registry_frame_rate_is_read_as_a_template_frame_rate(string registry, string? expected)
    {
        Assert.Equal(expected, RegistryValueMapper.FrameRate(registry));
    }

    [Theory]
    [InlineData("2.39 (Scope)", "2.39", "Scope")]
    [InlineData("1.85 (Flat)", "1.85", "Flat")]
    [InlineData("1.77", "1.77", null)]
    [InlineData("177", "1.77", null)]
    [InlineData("1.78", "1.78", null)]
    public void A_registry_ratio_gives_the_aspect_ratio_and_the_cinema_mode(string registry, string ratio, string? mode)
    {
        Assert.Equal(ratio, RegistryValueMapper.AspectRatio(registry));
        Assert.Equal(mode, RegistryValueMapper.CinemaMode(registry));
    }

    [Theory]
    [InlineData(@"FTR\PRHQ\FILM_FTR_HD.mov", false, 0, null, "Video")]
    [InlineData(@"FTR\AUDIO\FILM_6CH.L.wav", false, 1, null, "Audio")]
    [InlineData(@"SUBTITLE\FTR\ST_EN\FILM_ANG.stl", false, 2, null, "Subtitle")]
    [InlineData(@"FTR\DCP\FILM_FTR_S_FR-EN_51_2K_OV", true, 7, "DCP 2K", "DcpPackage")]
    [InlineData(@"PROMO\TLR130\DCDM\FR-EN\FLAT", true, 6, null, "ImageSequence")]
    [InlineData(@"CATALOG_EUROPA-CORP\FILM", true, 8, null, "Generic")]
    public void The_asset_type_comes_from_the_shape_and_extension_of_the_element(
        string path, bool isFolder, int category, string? codec, string expected)
    {
        Assert.Equal(expected, RegistryValueMapper.AssetType(path, isFolder, category, codec));
    }

    [Theory]
    [InlineData("FTR", @"FTR\PRHQ\x.mov", "Feature")]
    [InlineData("TLR-01", @"PROMO\TLR150\DCP\x", "Trailer")]
    [InlineData(null, @"BONUS\EPK\x.mov", "EPK")]
    [InlineData(null, @"PROMO\TLR130\INTER\x.mov", "Trailer")]
    [InlineData(null, @"BONUS\EXTRAITS\x.mov", "Clip")]
    [InlineData(null, @"SUBTITLE\20180321_FR_24FPS\x.stl", "Feature")]
    [InlineData(null, @"SUBTITLE\TLR150\EN\x.stl", "Trailer")]
    [InlineData(null, @"MISC\x.mov", null)]
    public void The_content_type_comes_from_the_element_type_else_from_its_folders(string? type, string path, string? expected)
    {
        Assert.Equal(expected, RegistryValueMapper.ContentType(type, path));
    }

    [Theory]
    [InlineData("Feature", "FTR", @"FTR\PRHQ\x.mov", "Original")]
    [InlineData("Feature", null, @"SUBTITLE\FTR\ST_EN\x.stl", "Original")]
    [InlineData("Trailer", "TLR-01", @"PROMO\TLR150\DCP\x", "TLR-01")]
    [InlineData("Trailer", null, @"PROMO\TLR130\PR444\x.mov", "TLR130")]
    [InlineData("Trailer", "TLR", @"TLR\PRHQ\x.mov", "PRHQ")]
    [InlineData("EPK", null, @"BONUS\EPK\x.mov", "EPK")]
    [InlineData("Promo", null, @"PROMO\CUTDOWN_30s\PR44\x.mov", "CUTDOWN_30S")]
    [InlineData(null, null, @"MISC\x.mov", null)]
    public void The_content_version_groups_the_feature_each_trailer_and_each_bonus_folder(
        string? contentType, string? registryType, string path, string? expected)
    {
        Assert.Equal(expected, RegistryValueMapper.ContentVersion(contentType, registryType, path));
    }

    [Theory]
    [InlineData("PRHQ", ".mov", "ProRes")]
    [InlineData("PR444", ".mov", "ProRes")]
    [InlineData(null, ".mxf", null)]
    [InlineData(null, ".wav", "PCM")]
    [InlineData("DCP 2K", "", "JPEG 2000")]
    public void The_codec_comes_from_the_registry_codec_else_from_the_extension(string? codec, string extension, string? expected)
    {
        Assert.Equal(expected, RegistryValueMapper.Codec(codec, extension));
    }
}
