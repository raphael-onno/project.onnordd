namespace MediaspotIngest.Core.Domain;

/// <summary>
/// Title-level data shared by every item of a registry title folder (the old tool's "custom titles").
/// Filled once per title, then copied into each item's title columns.
/// </summary>
public sealed class TitleInfo
{
    public string ProfileName { get; set; } = "";

    /// <summary>The registry title folder, e.g. <c>20-ANS-D-ECART</c>.</summary>
    public string TitleKey { get; set; } = "";

    /// <summary>The Legacy Importer "Title ID" — the customer id of the Mediaspot title.</summary>
    public string? TitleCustomId { get; set; }

    /// <summary>The title's Mediaspot id ("VDM Title ID", column titleId), once created or found in Mediaspot.</summary>
    public long? VdmTitleId { get; set; }

    public string MainTitle { get; set; } = "";

    public string TitleType { get; set; } = "Movie";

    public string? OriginalLanguage { get; set; }

    public string? PictureFormat { get; set; }

    public int? ProductionYear { get; set; }

    public string? SeasonNumber { get; set; }

    public int? EpisodesCount { get; set; }

    public bool IsSeason => string.Equals(TitleType, "Season", StringComparison.OrdinalIgnoreCase);
}
