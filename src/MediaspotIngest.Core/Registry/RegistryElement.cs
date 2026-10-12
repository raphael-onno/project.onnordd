namespace MediaspotIngest.Core.Registry;

/// <summary>One element of the EuropaCorp registry: a file, or a folder for DCPs, image sequences and documents.</summary>
public sealed class RegistryElement
{
    /// <summary>Stable id (md5 of category|path) shared with the registry page.</summary>
    public string Id { get; init; } = "";

    public string TitleKey { get; init; } = "";

    public string TitleName { get; init; } = "";

    /// <summary>Relative to the title folder, with Windows separators.</summary>
    public string RelativePath { get; init; } = "";

    public int Category { get; init; }

    public double SizeGb { get; init; }

    public bool IsFolder { get; init; }

    public int? FileCount { get; init; }

    /// <summary>ltfs, retro or part.</summary>
    public string Source { get; init; } = "";

    public IReadOnlyList<string> Tapes { get; init; } = [];

    /// <summary>VDM's proposal: ingest (mediaspot®), lto8, excl (to delete) or todo (to decide).</summary>
    public string Recommendation { get; init; } = "";

    /// <summary>The client's decision, when one was made, with the same codes (ingest, lto8, excl).</summary>
    public string? Decision { get; init; }

    public RegistryMetadata Metadata { get; init; } = new();

    /// <summary>The client's decision wins; without one, VDM's proposal.</summary>
    public bool IsToIngest => IsIngestCode(Decision is { Length: > 0 } ? Decision : Recommendation);

    private static bool IsIngestCode(string code) =>
        code.Equals("ingest", StringComparison.OrdinalIgnoreCase) || code.StartsWith("mediaspot", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Technical facts the registry parsed from the file and folder names.</summary>
public sealed class RegistryMetadata
{
    /// <summary>Element type from the name: FTR, TLR, TLR-01, EPK…</summary>
    public string? Type { get; set; }

    /// <summary>PRHQ, PR444, PRORES422HQ, XDCAM, DCP 2K…</summary>
    public string? Codec { get; set; }

    /// <summary>Audio-subtitle languages, e.g. <c>FR-XX</c>, <c>FR-EN</c>.</summary>
    public string? Language { get; set; }

    /// <summary>e.g. <c>2.39</c>, <c>1.85 (Flat)</c>, <c>2.39 (Scope)</c>.</summary>
    public string? Ratio { get; set; }

    /// <summary>e.g. <c>24 fps</c>.</summary>
    public string? FrameRate { get; set; }

    /// <summary>SD, HD, UHD, 2K, 4K.</summary>
    public string? Definition { get; set; }

    /// <summary>Audio layout, e.g. <c>51</c>, <c>2TRK-MNE-6TRK</c>.</summary>
    public string? Audio { get; set; }

    /// <summary>Textless, OV, VF…</summary>
    public string? Version { get; set; }

    /// <summary>Date found in the name, dd/MM/yyyy.</summary>
    public string? Date { get; set; }
}
