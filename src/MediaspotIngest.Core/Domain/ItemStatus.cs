namespace MediaspotIngest.Core.Domain;

public enum ItemStatus
{
    /// <summary>Invalid cells or file not found.</summary>
    FixNeeded,

    /// <summary>All cells valid and file found: can be exported.</summary>
    Ready,

    /// <summary>Exported, waiting for the user to run the legacy import in Mediaspot.</summary>
    Exported,

    /// <summary>The asset exists in Mediaspot but is not accepted yet.</summary>
    Pending,

    /// <summary>Accepted by Mediaspot.</summary>
    Done,
}

public static class ItemStatusRules
{
    public const string MediaspotAccepted = "Accepted";

    private static readonly string[] MediaspotPendingStatuses = ["New", "ParsedAndProbbed"];

    /// <summary>Same precedence as the old tool: what Mediaspot says first, then local readiness.</summary>
    public static ItemStatus Evaluate(IngestItem item, bool allCellsValid)
    {
        if (item.MediaspotAssetId is > 0)
        {
            if (string.Equals(item.MediaspotStatus, MediaspotAccepted, StringComparison.OrdinalIgnoreCase))
                return ItemStatus.Done;
            if (MediaspotPendingStatuses.Contains(item.MediaspotStatus, StringComparer.OrdinalIgnoreCase))
                return ItemStatus.Pending;
        }

        var fileAvailable = item.Location is FileLocation.Source or FileLocation.Ingest;
        if (!allCellsValid || !fileAvailable)
            return ItemStatus.FixNeeded;

        return item.ExportedAt is null ? ItemStatus.Ready : ItemStatus.Exported;
    }
}
