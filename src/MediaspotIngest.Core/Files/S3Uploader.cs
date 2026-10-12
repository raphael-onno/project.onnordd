using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using MediaspotIngest.Core.Configuration;

namespace MediaspotIngest.Core.Files;

/// <summary>
/// Writes media into the provider's temp folder in its S3 bucket: the cloud equivalent of copying into the
/// on-premise temp folder. Mediaspot's Legacy Importer then finds the file there (it sees the bucket as a disk),
/// creates the request and copies it to the final storage, exactly as for an on-premise provider.
/// </summary>
public sealed class S3Uploader(S3Settings settings, string secretAccessKey) : IDisposable
{
    private readonly AmazonS3Client _client = new(
        new BasicAWSCredentials(settings.AccessKeyId.Trim(), secretAccessKey),
        new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region.Trim()) });

    /// <summary>Checks the key can list the temp folder (read access is enough to tell the settings are right).</summary>
    public async Task<string> CheckAsync(CancellationToken ct = default)
    {
        var response = await _client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = settings.Bucket.Trim(),
            Prefix = settings.KeyFor(""),
            MaxKeys = 5,
        }, ct);
        return $"Accès au bucket {settings.Bucket} OK ({response.KeyCount} objet(s) visibles dans {settings.KeyFor("")}).";
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(settings.Bucket.Trim(), key, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Uploads a file, or every file of a folder under the folder's name. Never overwrites: an existing object stops
    /// the upload. Large files go in parallel parts. The source is deleted afterwards only when <paramref name="removeSource"/>.
    /// </summary>
    public async Task UploadAsync(string source, string name, bool isFolder, bool removeSource, IProgress<double>? progress, CancellationToken ct)
    {
        var files = isFolder
            ? Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Select(f => (Path: f, Key: settings.KeyFor(Path.Combine(name, Path.GetRelativePath(source, f)))))
                .ToList()
            : [(Path: source, Key: settings.KeyFor(name))];

        foreach (var file in files)
        {
            if (await ExistsAsync(file.Key, ct))
                throw new IOException($"« {file.Key} » existe déjà dans le bucket : il n'est pas écrasé.");
        }

        var total = Math.Max(1, files.Sum(f => new FileInfo(f.Path).Length));
        long done = 0;
        using var transfer = new TransferUtility(_client, new TransferUtilityConfig { ConcurrentServiceRequests = Math.Max(1, settings.ConcurrentParts) });
        foreach (var file in files)
        {
            var length = new FileInfo(file.Path).Length;
            var request = new TransferUtilityUploadRequest
            {
                BucketName = settings.Bucket.Trim(),
                Key = file.Key,
                FilePath = file.Path,
                PartSize = PartSize(length),
            };
            var before = done;
            request.UploadProgressEvent += (_, e) => progress?.Report((before + e.TransferredBytes) / (double)total);
            await transfer.UploadAsync(request, ct);
            done += length;
        }

        if (removeSource)
        {
            if (isFolder)
                Directory.Delete(source, recursive: true);
            else
                File.Delete(source);
        }

        progress?.Report(1);
    }

    /// <summary>S3 allows 10 000 parts per object: grow the part size for very large masters.</summary>
    internal static long PartSize(long length)
    {
        const long minimum = 64L * 1024 * 1024;
        var needed = (long)Math.Ceiling(length / 9000.0);
        return Math.Max(minimum, needed);
    }

    public void Dispose() => _client.Dispose();
}
