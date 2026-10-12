using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.Files;

/// <summary>Where an item's media is, given the profile's storages.</summary>
public sealed class MediaFiles(PlatformProfile profile)
{
    /// <summary>Restored from LTO: <c>{SourceRoot}\{title folder}\{relative path}</c>.</summary>
    public string SourcePath(IngestItem item) => Path.Combine(profile.SourceRootPath, item.TitleKey, item.RelativePath);

    /// <summary>Mediaspot's importer looks for <c>originalFileName</c> directly in the provider's temp folder.</summary>
    public string IngestPath(IngestItem item) => profile.TransferTarget switch
    {
        TransferTarget.Aspera => new AsperaUploader(profile.Aspera).Destination(item.FileName),
        TransferTarget.S3 => profile.S3.Display(item.FileName),
        _ => Path.Combine(profile.IngestDirectoryPath, item.FileName),
    };

    /// <summary>The temp folder is remote (Aspera account or S3 bucket): it cannot be listed like a Windows folder.</summary>
    private bool RemoteTarget => profile.TransferTarget is TransferTarget.Aspera or TransferTarget.S3;

    public string? DonePath(IngestItem item) =>
        RemoteTarget || string.IsNullOrWhiteSpace(profile.DoneDirectoryPath)
            ? null
            : Path.Combine(profile.DoneDirectoryPath, item.FileName);

    /// <summary>Looks the media up (ingest folder first: once transferred, that copy is the one that counts) and records its size.</summary>
    public FileLocation Locate(IngestItem item)
    {
        if (RemoteTarget)
            return LocateForRemoteTarget(item);

        if (TryMeasure(IngestPath(item), item.IsFolder, out var size))
            return Record(item, FileLocation.Ingest, size);
        if (TryMeasureSource(item, out size))
            return Record(item, FileLocation.Source, size);
        if (DonePath(item) is { } done && TryMeasure(done, item.IsFolder, out size))
            return Record(item, FileLocation.Done, size);

        return NotFound(item);
    }

    /// <summary>The remote temp folder is not listed from here: a successful upload is what tells the file is there.</summary>
    private FileLocation LocateForRemoteTarget(IngestItem item)
    {
        var inSource = TryMeasureSource(item, out var size);
        if (item.TransferredAt is not null)
        {
            // Checked before the upload; the copy in the bucket cannot be read from here.
            item.Location = FileLocation.Ingest;
            if (inSource)
                item.ActualSizeBytes = size;
            return item.Location;
        }

        return inSource ? Record(item, FileLocation.Source, size) : NotFound(item);
    }

    private bool TryMeasureSource(IngestItem item, out long size)
    {
        size = 0;
        return !string.IsNullOrWhiteSpace(profile.SourceRootPath) && TryMeasure(SourcePath(item), item.IsFolder, out size);
    }

    private FileLocation Record(IngestItem item, FileLocation location, long size)
    {
        item.Location = location;
        item.ActualSizeBytes = size;
        var path = location switch
        {
            FileLocation.Ingest => IngestPath(item),
            FileLocation.Done => DonePath(item)!,
            _ => SourcePath(item),
        };
        item.FileWarning = FileChecks.Check(item, path);
        return location;
    }

    private static FileLocation NotFound(IngestItem item)
    {
        item.Location = FileLocation.NotFound;
        item.ActualSizeBytes = null;
        item.FileWarning = null;
        return item.Location;
    }

    private static bool TryMeasure(string path, bool isFolder, out long size)
    {
        size = 0;
        try
        {
            if (isFolder)
            {
                var directory = new DirectoryInfo(path);
                if (!directory.Exists)
                    return false;
                size = directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                return true;
            }

            var file = new FileInfo(path);
            if (!file.Exists)
                return false;
            size = file.Length;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
