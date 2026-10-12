using MediaspotIngest.Core.Registry;

namespace MediaspotIngest.Core.Domain;

/// <summary>One media file to ingest into Mediaspot, i.e. one row of the Legacy Importer sheet.</summary>
public sealed class IngestItem
{
    /// <summary>The registry element id — stable and unique, unlike the file name the old tool keyed on.</summary>
    public string Id { get; set; } = "";

    public string ProfileName { get; set; } = "";

    public int BatchNumber { get; set; }

    /// <summary>The registry title folder, e.g. <c>20-ANS-D-ECART</c>.</summary>
    public string TitleKey { get; set; } = "";

    /// <summary>Path relative to the title folder, as the registry lists it.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>DCPs, image sequences and document folders are ingested as one folder.</summary>
    public bool IsFolder { get; set; }

    /// <summary>Registry category (0 masters, 1 audio, 2 subtitles, 3/6 image sequences, 7 DCP…).</summary>
    public int Category { get; set; }

    public RegistryMetadata Metadata { get; set; } = new();

    /// <summary>Size announced by the registry (LTFS/Retrospect catalogues), to check the restored file.</summary>
    public long? ExpectedSizeBytes { get; set; }

    /// <summary>Where the registry found the element: ltfs, retro or part. Retrospect sizes are not reliable.</summary>
    public string? Source { get; set; }

    /// <summary>Result of the file checks (size, STL subtitle count) the last time the file was located; null when fine.</summary>
    public string? FileWarning { get; set; }

    /// <summary>Size measured on disk once the file was found.</summary>
    public long? ActualSizeBytes { get; set; }

    /// <summary>Legacy Importer cell values by column name. Persisted: an edit is never lost.</summary>
    public Dictionary<string, string> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public FileLocation Location { get; set; } = FileLocation.Unknown;

    /// <summary>When the media reached the provider's temp folder. The only trace for an Aspera upload, which we cannot list back.</summary>
    public DateTime? TransferredAt { get; set; }

    public DateTime? ExportedAt { get; set; }

    public long? MediaspotAssetId { get; set; }

    /// <summary>Last status read from Mediaspot (New, ParsedAndProbbed, Accepted, Deleted…).</summary>
    public string? MediaspotStatus { get; set; }

    public string FileName => Path.GetFileName(RelativePath);

    public static IngestItem FromRegistry(RegistryElement element, string profileName, int batchNumber) => new()
    {
        Id = element.Id,
        ProfileName = profileName,
        BatchNumber = batchNumber,
        TitleKey = element.TitleKey,
        RelativePath = element.RelativePath,
        IsFolder = element.IsFolder,
        Category = element.Category,
        Metadata = element.Metadata,
        Source = element.Source,
        ExpectedSizeBytes = element.SizeGb > 0 ? (long)(element.SizeGb * 1024 * 1024 * 1024) : null,
    };

    public string? GetCell(string column) => Cells.TryGetValue(column.Trim(), out var value) ? value : null;

    public void SetCell(string column, string? value) => Cells[column.Trim()] = value ?? "";
}

public enum FileLocation
{
    /// <summary>Not looked for yet.</summary>
    Unknown,

    /// <summary>Not in the restore area nor in the ingest folder.</summary>
    NotFound,

    /// <summary>Restored from LTO, waiting to be transferred.</summary>
    Source,

    /// <summary>In the Aspera ingest folder, ready for the legacy import.</summary>
    Ingest,

    /// <summary>Moved out of the ingest folder after Mediaspot accepted it.</summary>
    Done,
}
