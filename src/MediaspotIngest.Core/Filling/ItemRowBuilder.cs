using System.Globalization;
using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.Filling;

/// <summary>
/// Pre-fills an item's Legacy Importer row — what the old tool's "Autofill Xlsx Datas" did, from the registry
/// instead of Dalet. Only columns present in the platform template are written, and picklist values are
/// written with the template's exact spelling.
/// </summary>
public sealed class ItemRowBuilder(LegacyTemplate template, PlatformProfile profile)
{
    /// <summary>Rebuilds every cell. Values the user typed by hand are lost: call it on new items, or on demand.</summary>
    public void Fill(IngestItem item, TitleInfo? title)
    {
        item.Cells.Clear();

        foreach (var (tagName, value) in profile.DefaultCellValues)
            Set(item, tagName, value, guessed: false);

        FillTitle(item, title);
        FillAsset(item);
    }

    /// <summary>Re-applies only the title columns, after the title was edited.</summary>
    public void FillTitle(IngestItem item, TitleInfo? title)
    {
        if (title is null)
            return;

        Set(item, "titleCustomerId", title.TitleCustomId, guessed: false);

        // titleId is an importer column ("A"), normally filled by its Titles step. When the title was created
        // from here, its Mediaspot id is known: writing it lets the asset import attach to it directly.
        if (template.Find("titleId") is { } titleIdColumn)
        {
            if (title.VdmTitleId is > 0)
                item.SetCell(titleIdColumn.TagName, title.VdmTitleId.Value.ToString(CultureInfo.InvariantCulture));
            else
                item.Cells.Remove(titleIdColumn.TagName);
        }

        Set(item, "primaryTitle", title.MainTitle, guessed: false);
        Set(item, "productionDate", title.ProductionYear?.ToString(CultureInfo.InvariantCulture), guessed: false);
        Set(item, "titleOriginalLanguage", title.OriginalLanguage ?? profile.DefaultOriginalLanguage, guessed: false);
        Set(item, "titleOriginalPictureFormat", title.PictureFormat ?? profile.DefaultPictureFormat, guessed: false);
        Set(item, "titleType", title.TitleType, guessed: false);
        if (title.IsSeason)
        {
            Set(item, "serieSeasonNumber", title.SeasonNumber, guessed: false);
            Set(item, "serieSeasonEpisodeTotalNumber", title.EpisodesCount?.ToString(CultureInfo.InvariantCulture), guessed: false);
        }
    }

    private void FillAsset(IngestItem item)
    {
        var metadata = item.Metadata;
        var extension = item.IsFolder ? "" : Path.GetExtension(item.RelativePath);

        // The importer looks for originalFileName in the provider's temp folder; customerOriginalFileName keeps
        // the client's name once ingest renames the file; streamId lets us find the created asset again.
        Set(item, "originalFileName", item.FileName);
        Set(item, "customerOriginalFileName", item.FileName);
        Set(item, "streamId", item.Id);

        var assetType = RegistryValueMapper.AssetType(item.RelativePath, item.IsFolder, item.Category, metadata.Codec);
        Set(item, "assetType", assetType);
        var contentType = RegistryValueMapper.ContentType(metadata.Type, item.RelativePath);
        Set(item, "contentType", contentType);
        // Free text: only fill it when the profile did not fix a value for every item.
        if (!profile.DefaultCellValues.ContainsKey("contentVersion"))
            Set(item, "contentVersion", RegistryValueMapper.ContentVersion(contentType, metadata.Type, item.RelativePath));
        Set(item, "techWrapper", RegistryValueMapper.Wrapper(extension));
        Set(item, "techCodec", RegistryValueMapper.Codec(metadata.Codec, extension));

        var (audioLanguage, subtitleLanguage) = RegistryValueMapper.Languages(metadata.Language);
        if (assetType is "Video" or "DcpPackage" or "ImfPackage")
        {
            Set(item, "languagePicture", audioLanguage);
            Set(item, "languageAudio", audioLanguage);
            Set(item, "languageSubtitle", subtitleLanguage);
            Set(item, "subtitleIsBurned", subtitleLanguage is null ? null : "True");
        }
        else if (assetType == "Audio")
        {
            Set(item, "languageAudio", audioLanguage);
        }
        else if (assetType == "Subtitle")
        {
            Set(item, "languageSubtitle", subtitleLanguage ?? audioLanguage);
        }

        Set(item, "techFrameRate", RegistryValueMapper.FrameRate(metadata.FrameRate));
        Set(item, "videoProfile", metadata.Definition?.ToUpperInvariant());
        if (string.Equals(metadata.Definition, "HD", StringComparison.OrdinalIgnoreCase))
            Set(item, "techColorSpace", "REC709");

        Set(item, "originalPictureFormat", RegistryValueMapper.AspectRatio(metadata.Ratio));
        Set(item, "pictureOverallCinemaMode", RegistryValueMapper.CinemaMode(metadata.Ratio));
        Set(item, "pictureOverallVersion", RegistryValueMapper.PictureVersion(metadata.Version, item.RelativePath));
        Set(item, "audioMix", RegistryValueMapper.AudioMix(metadata.Audio));

        if (assetType == "DcpPackage")
        {
            var version = metadata.Version?.ToUpperInvariant();
            Set(item, "dcpVersion", version is "OV" or "VF" ? version : null);
        }
    }

    /// <summary>
    /// Writes the value if the template has that column; null leaves the previous value.
    /// A value deduced from file names (<paramref name="guessed"/>) that the picklist does not allow is dropped
    /// rather than written: an empty optional cell is better than one the importer rejects.
    /// Values typed by the user (profile defaults, titles) are always written, so a mistake shows up in validation.
    /// </summary>
    private void Set(IngestItem item, string tagName, string? value, bool guessed = true)
    {
        if (value is null)
            return;

        var column = template.Find(tagName);
        if (column is null || column.IsAutomatic)
            return;

        var canonical = CellValues.Canonicalize(column, value);
        if (guessed && column.ValueKind == ColumnValueKind.Picklist && !CellValues.IsAllowed(column, canonical))
            return;

        item.SetCell(column.TagName, canonical);
    }
}
