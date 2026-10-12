using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.Files;

/// <summary>
/// The file moves an action is about to make, computed first so the user can review them — the old tool moved
/// files straight away and swallowed the errors.
/// </summary>
public sealed class TransferPlan
{
    private readonly Func<TransferStep, IProgress<double>?, CancellationToken, Task> _execute;

    private TransferPlan(Func<TransferStep, IProgress<double>?, CancellationToken, Task> execute) => _execute = execute;

    public required FileTransferMode Mode { get; init; }

    /// <summary>"Aspera" or "Dossier", for the confirmation screen.</summary>
    public required string Method { get; init; }

    public required IReadOnlyList<TransferStep> Steps { get; init; }

    public IEnumerable<TransferStep> Runnable => Steps.Where(s => s.Problem is null);

    public long TotalBytes => Runnable.Sum(s => s.Item.ActualSizeBytes ?? 0);

    /// <summary>
    /// Source (LTO restore) → provider temp folder, for items found in the restore area.
    /// <paramref name="secret"/> is the Aspera password or the S3 secret access key, depending on the target.
    /// </summary>
    public static TransferPlan ToIngest(PlatformProfile profile, IEnumerable<IngestItem> items, string? secret = null)
    {
        var files = new MediaFiles(profile);
        var folderTarget = profile.TransferTarget == TransferTarget.Folder;
        var steps = items
            .Where(i => i.Location == FileLocation.Source)
            .Select(i => new TransferStep(i, files.SourcePath(i), files.IngestPath(i),
                // Remote targets never overwrite either, but check at upload time (they cannot be listed here).
                folderTarget && Exists(files.IngestPath(i)) ? "La destination existe déjà." : null))
            .ToList();
        FlagDuplicateNames(steps);
        var move = profile.TransferMode == FileTransferMode.Move;

        switch (profile.TransferTarget)
        {
            case TransferTarget.Aspera:
                var aspera = new AsperaUploader(profile.Aspera);
                return new TransferPlan((step, progress, ct) => aspera.UploadAsync(step.From, move, secret ?? "", progress, ct))
                {
                    Mode = profile.TransferMode, Method = "Aspera", Steps = steps,
                };

            case TransferTarget.S3:
                return new TransferPlan(async (step, progress, ct) =>
                {
                    using var s3 = new S3Uploader(profile.S3, secret ?? "");
                    await s3.UploadAsync(step.From, step.Item.FileName, step.Item.IsFolder, move, progress, ct);
                })
                {
                    Mode = profile.TransferMode, Method = "S3", Steps = steps,
                };

            default:
                return new TransferPlan((step, _, _) => Task.Run(() => CopyOrMove(step, profile.TransferMode)))
                {
                    Mode = profile.TransferMode, Method = "Dossier", Steps = steps,
                };
        }
    }

    /// <summary>Provider temp folder → done folder, for items Mediaspot accepted. Always a move, folder target only.</summary>
    public static TransferPlan ToDone(PlatformProfile profile, IEnumerable<IngestItem> items)
    {
        var files = new MediaFiles(profile);
        var steps = items
            .Where(i => i.Location == FileLocation.Ingest && files.DonePath(i) is not null)
            .Select(i => new TransferStep(i, files.IngestPath(i), files.DonePath(i)!, Exists(files.DonePath(i)!) ? "La destination existe déjà." : null))
            .ToList();
        return new TransferPlan((step, _, _) => Task.Run(() => CopyOrMove(step, FileTransferMode.Move)))
        {
            Mode = FileTransferMode.Move, Method = "Dossier", Steps = steps,
        };
    }

    /// <summary>Runs the runnable steps one by one; a failed step is reported and the next one goes on.</summary>
    public async Task<IReadOnlyList<TransferStep>> RunAsync(IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        var done = new List<TransferStep>();
        var runnable = Runnable.ToList();

        for (var i = 0; i < runnable.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = runnable[i];
            var index = i;
            progress?.Report(new TransferProgress(i, runnable.Count, step.Item.FileName, 0));
            var fileProgress = new Progress<double>(p => progress?.Report(new TransferProgress(index, runnable.Count, step.Item.FileName, p)));

            try
            {
                await _execute(step, fileProgress, cancellationToken);
                step.Item.TransferredAt = DateTime.Now;
                done.Add(step);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Amazon.Runtime.AmazonServiceException)
            {
                done.Add(step with { Problem = e.Message });
            }
        }

        progress?.Report(new TransferProgress(runnable.Count, runnable.Count, null, 1));
        return done;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>The temp folder is flat: two files with the same name in different folders would collide.</summary>
    private static void FlagDuplicateNames(List<TransferStep> steps)
    {
        var duplicates = steps.GroupBy(s => s.To, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
        for (var i = 0; i < steps.Count; i++)
        {
            if (duplicates.Contains(steps[i]))
                steps[i] = steps[i] with { Problem = "Même nom de fichier qu'un autre item du lot : renommer avant le transfert." };
        }
    }

    private static void CopyOrMove(TransferStep step, FileTransferMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(step.To)!);

        if (step.Item.IsFolder)
        {
            // Directory.Move cannot cross shares: copy, then delete the source once everything is there.
            CopyDirectory(step.From, step.To);
            if (mode == FileTransferMode.Move)
                Directory.Delete(step.From, recursive: true);
            return;
        }

        if (mode == FileTransferMode.Move)
            File.Move(step.From, step.To, overwrite: false);
        else
            File.Copy(step.From, step.To, overwrite: false);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
    }
}

public sealed record TransferStep(IngestItem Item, string From, string To, string? Problem);

/// <summary><paramref name="FileFraction"/> is the progress of the current file, from 0 to 1.</summary>
public sealed record TransferProgress(int Done, int Total, string? Current, double FileFraction);
