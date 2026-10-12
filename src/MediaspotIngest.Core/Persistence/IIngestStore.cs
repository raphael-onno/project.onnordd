using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.Persistence;

/// <summary>Batches, items and titles of one platform profile.</summary>
public interface IIngestStore
{
    Task<IReadOnlyList<IngestBatch>> GetBatchesAsync(string profile, CancellationToken ct = default);

    Task SaveBatchAsync(IngestBatch batch, CancellationToken ct = default);

    Task<IReadOnlyList<IngestItem>> GetItemsAsync(string profile, int? batchNumber = null, CancellationToken ct = default);

    Task SaveItemsAsync(IEnumerable<IngestItem> items, CancellationToken ct = default);

    Task DeleteItemsAsync(IEnumerable<IngestItem> items, CancellationToken ct = default);

    Task<IReadOnlyList<TitleInfo>> GetTitlesAsync(string profile, CancellationToken ct = default);

    Task SaveTitlesAsync(IEnumerable<TitleInfo> titles, CancellationToken ct = default);
}
