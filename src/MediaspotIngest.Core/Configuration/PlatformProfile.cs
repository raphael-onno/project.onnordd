namespace MediaspotIngest.Core.Configuration;

/// <summary>
/// One Mediaspot platform (a client, e.g. EuropaCorp) and every path and default the ingest needs for it.
/// </summary>
public sealed class PlatformProfile
{
    public string Name { get; set; } = "";

    // --- Mediaspot connection -------------------------------------------------------------

    public long PlatformId { get; set; }

    public long ProviderId { get; set; }

    public string Login { get; set; } = "";

    /// <summary>Encrypted with DPAPI.</summary>
    public string? ProtectedPassword { get; set; }

    // --- Storages and paths ---------------------------------------------------------------

    /// <summary>Where the media are restored from LTO (e.g. on the Qumulo). Registry paths are relative to <c>{SourceRootPath}\{title folder}</c>.</summary>
    public string SourceRootPath { get; set; } = "";

    /// <summary>How media reach the provider's temp folder, which the Legacy Importer reads.</summary>
    public TransferTarget TransferTarget { get; set; } = TransferTarget.Folder;

    /// <summary>Folder target: the provider's temp folder as a Windows path (on-premise platforms).</summary>
    public string IngestDirectoryPath { get; set; } = "";

    /// <summary>Aspera target: the provider's Aspera account, which writes into its storage (an S3 bucket on cloud platforms).</summary>
    public AsperaSettings Aspera { get; set; } = new();

    /// <summary>S3 target: direct write into the provider's temp folder in its S3 bucket (cloud platforms).</summary>
    public S3Settings S3 { get; set; } = new();

    /// <summary>Where media are moved once Mediaspot accepted them. Optional.</summary>
    public string? DoneDirectoryPath { get; set; }

    /// <summary>The Legacy Importer XLSX template dedicated to this platform.</summary>
    public string TemplatePath { get; set; } = "";

    /// <summary>Where generated Legacy Importer XLSX files are written.</summary>
    public string ExportDirectoryPath { get; set; } = "";

    /// <summary>The ingest config pack chosen in the Legacy Importer (NRJ12: "RDD Pack"): it gives each asset type its template.</summary>
    public long? ConfigIngestPackId { get; set; }

    public string? ConfigIngestPackName { get; set; }

    /// <summary>The platform storage of kind LegacyImporter the XLSX is uploaded to; found automatically when empty.</summary>
    public long? LegacyImporterStorageId { get; set; }

    /// <summary>Copy keeps the restored source intact; Move frees the restore area (what the old tool did).</summary>
    public FileTransferMode TransferMode { get; set; } = FileTransferMode.Copy;

    // --- Defaults applied when a value cannot be derived ---------------------------------

    public string DefaultOriginalLanguage { get; set; } = "FRA";

    public string DefaultPictureFormat { get; set; } = "1.77";

    /// <summary>
    /// Fixed values by template tagName, applied to every item before the registry-derived ones
    /// (e.g. <c>assetClass = MEZZ</c>, <c>contentVersion = Original</c>, <c>isLegacyImport = True</c>).
    /// </summary>
    public Dictionary<string, string> DefaultCellValues { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assetClass"] = "MEZZ",
        ["isLegacyImport"] = "True",
        ["laboratoryFrom"] = "VDM",
    };

    // --- Mediaspot metadata used to find the asset created by the import ----------------

    public string StreamIdTagName { get; set; } = "streamId";

    public string CustomerOriginalFileNameTagName { get; set; } = "customerOriginalFileName";

    public PlatformProfile Clone()
    {
        var clone = (PlatformProfile)MemberwiseClone();
        clone.DefaultCellValues = new Dictionary<string, string>(DefaultCellValues, StringComparer.OrdinalIgnoreCase);
        clone.Aspera = Aspera.Clone();
        clone.S3 = S3.Clone();
        return clone;
    }
}

public enum FileTransferMode
{
    Copy,
    Move,
}

public enum TransferTarget
{
    /// <summary>Copy/move into a Windows folder (the provider's temp folder on an on-premise storage).</summary>
    Folder,

    /// <summary>Upload with Aspera (ascp) into the provider's storage, e.g. its S3 bucket.</summary>
    Aspera,

    /// <summary>Write straight into the provider's temp folder in its S3 bucket, with an AWS access key.</summary>
    S3,
}

public sealed class S3Settings
{
    /// <summary>AWS region of the bucket, e.g. eu-west-1.</summary>
    public string Region { get; set; } = "";

    public string Bucket { get; set; } = "";

    /// <summary>Key prefix of the provider's temp folder, e.g. MSPROOT/AsperaIngoing/asp-cloudawsdemo-759-vdmeuropa/temp/</summary>
    public string Prefix { get; set; } = "";

    public string AccessKeyId { get; set; } = "";

    /// <summary>Encrypted with DPAPI.</summary>
    public string? ProtectedSecretAccessKey { get; set; }

    /// <summary>Parallel parts per file for large uploads.</summary>
    public int ConcurrentParts { get; set; } = 8;

    public S3Settings Clone() => (S3Settings)MemberwiseClone();

    /// <summary>The object key of a file (or of a file inside a folder) in the temp folder.</summary>
    public string KeyFor(string relativeName)
    {
        var prefix = Prefix.Trim().Replace('\\', '/').Trim('/');
        var name = relativeName.Replace('\\', '/').TrimStart('/');
        return prefix.Length == 0 ? name : $"{prefix}/{name}";
    }

    public string Display(string relativeName) => $"s3://{Bucket}/{KeyFor(relativeName)}";

    /// <summary>Reads "s3://bucket/some/prefix/" (as Mediaspot shows the provider's import folder) into Bucket and Prefix.</summary>
    public bool TrySetFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = url[5..].Trim('/');
        var slash = rest.IndexOf('/');
        Bucket = slash < 0 ? rest : rest[..slash];
        Prefix = slash < 0 ? "" : rest[(slash + 1)..] + "/";
        return Bucket.Length > 0;
    }
}

public sealed class AsperaSettings
{
    public string Host { get; set; } = "";

    /// <summary>SSH port of the Aspera server; null keeps ascp's default.</summary>
    public int? SshPort { get; set; }

    public string Login { get; set; } = "";

    /// <summary>Encrypted with DPAPI.</summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>Destination on the Aspera account, i.e. the provider's temp folder as Aspera sees it.</summary>
    public string RemoteDirectory { get; set; } = "/temp";

    /// <summary>ascp target rate, e.g. 500m (Mbps). Yan's tools used 150m.</summary>
    public string TargetRate { get; set; } = "500m";

    /// <summary>Path to ascp.exe; empty to look for Aspera Connect in the usual places.</summary>
    public string? AscpPath { get; set; }

    public AsperaSettings Clone() => (AsperaSettings)MemberwiseClone();
}
