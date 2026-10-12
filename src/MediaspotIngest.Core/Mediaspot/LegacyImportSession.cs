using ClosedXML.Excel;
using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Export;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.Mediaspot;

/// <summary>
/// Runs Mediaspot's Legacy Importer from the tool, the way its web page does (tutorial NRJ12, "Importer le XLSX
/// dans Legacy Importer"): upload the XLSX, Check assets, Import assets — which creates the requests and moves or
/// uploads each file under its request id — then read the request ids back (the old "Update GraphIds").
/// </summary>
public sealed class LegacyImportSession
{
    /// <summary>The importer refuses more rows per call.</summary>
    public const int MaxRowsPerCall = 500;

    private readonly MediaspotClient _client;
    private readonly PlatformProfile _profile;

    private LegacyImportSession(MediaspotClient client, PlatformProfile profile, IReadOnlyList<IngestItem> items, string localPath, long storageId)
    {
        _client = client;
        _profile = profile;
        Items = items;
        LocalPath = localPath;
        StorageId = storageId;
    }

    /// <summary>The rows, in sheet order: item i is on row <see cref="LegacyTemplate.FirstDataRow"/> + i.</summary>
    public IReadOnlyList<IngestItem> Items { get; }

    public string LocalPath { get; }

    public string FileName => Path.GetFileName(LocalPath);

    public long StorageId { get; }

    public int LastRow => LegacyTemplate.FirstDataRow + Items.Count - 1;

    /// <summary>Writes the XLSX of the items and finds where to upload it.</summary>
    public static async Task<LegacyImportSession> PrepareAsync(
        MediaspotClient client, PlatformProfile profile, LegacyTemplate template, IReadOnlyList<IngestItem> items, string exportDirectory, string baseName, CancellationToken ct = default)
    {
        if (profile.ConfigIngestPackId is null)
            throw new InvalidOperationException("Aucune config pack d'ingest choisie pour ce profil (Configuration → 4).");
        if (profile.ProviderId <= 0)
            throw new InvalidOperationException("ID du provider non renseigné pour ce profil.");

        var storageId = profile.LegacyImporterStorageId ?? (await client.GetLegacyImporterStoragesAsync(ct)).FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Aucun stockage « LegacyImporter » trouvé sur la plateforme.");

        var path = LegacyImporterWriter.Write(template, items, exportDirectory, baseName);
        return new LegacyImportSession(client, profile, items, path, storageId);
    }

    public Task UploadAsync(CancellationToken ct = default) => _client.UploadToStorageAsync(StorageId, LocalPath, ct);

    /// <summary>"Check assets": Mediaspot checks every row — metadata values, and that each file is in the provider's temp.</summary>
    public Task<IReadOnlyList<RowReport>> CheckAsync(IProgress<double>? progress = null, CancellationToken ct = default) =>
        RunAsync(import: false, progress, ct);

    /// <summary>"Import assets": creates the requests and moves/uploads each file under its request id. Uses one import token per call.</summary>
    public Task<IReadOnlyList<RowReport>> ImportAsync(IProgress<double>? progress = null, CancellationToken ct = default) =>
        RunAsync(import: true, progress, ct);

    private async Task<IReadOnlyList<RowReport>> RunAsync(bool import, IProgress<double>? progress, CancellationToken ct)
    {
        var reports = new List<RowReport>();
        for (var first = LegacyTemplate.FirstDataRow; first <= LastRow; first += MaxRowsPerCall)
        {
            var last = Math.Min(first + MaxRowsPerCall - 1, LastRow);
            var result = await _client.RunLegacyImportAsync(
                new LegacyImportRequest(FileName, StorageId, first, last, _profile.ProviderId, _profile.ConfigIngestPackId!.Value, import), ct);
            reports.AddRange(result.Reports.Select(r => new RowReport(ItemAt(r.Row), r)));
            progress?.Report((double)(last - LegacyTemplate.FirstDataRow + 1) / Items.Count);
        }

        return reports;
    }

    /// <summary>
    /// Downloads the XLSX the importer rewrote and stores each row's request id (assetId) on its item.
    /// Rows are matched by position, checked against originalFileName.
    /// </summary>
    public async Task<int> ReadBackAssetIdsAsync(CancellationToken ct = default)
    {
        var importedPath = Path.Combine(Path.GetDirectoryName(LocalPath)!, Path.GetFileNameWithoutExtension(LocalPath) + "-importe.xlsx");
        await _client.DownloadFromLegacyImporterAsync(StorageId, FileName, importedPath, ct);
        return ApplyAssetIds(importedPath, Items);
    }

    internal static int ApplyAssetIds(string importedPath, IReadOnlyList<IngestItem> items)
    {
        using var workbook = new XLWorkbook(importedPath);
        var sheet = workbook.Worksheets.First();
        var columns = sheet.Row(LegacyTemplate.TagNameRow).CellsUsed()
            .GroupBy(c => c.GetString().Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        if (!columns.TryGetValue("assetId", out var assetIdColumn))
            throw new InvalidDataException("Le XLSX traité n'a pas de colonne assetId.");
        columns.TryGetValue("originalFileName", out var fileNameColumn);

        var applied = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var row = LegacyTemplate.FirstDataRow + i;
            if (fileNameColumn > 0 && !string.Equals(sheet.Cell(row, fileNameColumn).GetString().Trim(), items[i].FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!long.TryParse(sheet.Cell(row, assetIdColumn).GetString().Trim(), out var assetId) || assetId <= 0)
                continue;

            items[i].MediaspotAssetId = assetId;
            items[i].MediaspotStatus ??= "New";
            items[i].ExportedAt = DateTime.Now;
            applied++;
        }

        return applied;
    }

    private IngestItem? ItemAt(int row)
    {
        var index = row - LegacyTemplate.FirstDataRow;
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }
}

/// <summary>A line of the importer's report, tied to the item of its row when there is one.</summary>
public sealed record RowReport(IngestItem? Item, LegacyImportReport Report)
{
    public string FileName => Item?.FileName ?? (Report.Row > 0 ? $"ligne {Report.Row}" : "");

    public bool IsError => Report.IsError;
}
