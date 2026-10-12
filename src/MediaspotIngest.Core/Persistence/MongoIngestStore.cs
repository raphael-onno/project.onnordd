using MediaspotIngest.Core.Domain;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MediaspotIngest.Core.Persistence;

/// <summary>
/// Shared store in MongoDB, in collections of its own (<c>{prefix}.batches</c>, <c>.items</c>, <c>.titles</c>).
/// Documents are keyed by profile + business key, so two platforms never overwrite each other —
/// the old tool keyed items on the bare file name.
/// </summary>
public sealed class MongoIngestStore : IIngestStore
{
    private readonly IMongoCollection<Stored<IngestBatch>> _batches;
    private readonly IMongoCollection<Stored<IngestItem>> _items;
    private readonly IMongoCollection<Stored<TitleInfo>> _titles;

    public MongoIngestStore(string connectionString, string database, string collectionPrefix)
    {
        var db = new MongoClient(connectionString).GetDatabase(database);
        _batches = db.GetCollection<Stored<IngestBatch>>($"{collectionPrefix}.batches");
        _items = db.GetCollection<Stored<IngestItem>>($"{collectionPrefix}.items");
        _titles = db.GetCollection<Stored<TitleInfo>>($"{collectionPrefix}.titles");
    }

    /// <summary>Fails fast with a readable error when the connection settings are wrong.</summary>
    public async Task PingAsync(CancellationToken ct = default) =>
        await _batches.Database.RunCommandAsync((Command<MongoDB.Bson.BsonDocument>)"{ping:1}", cancellationToken: ct);

    public async Task<IReadOnlyList<IngestBatch>> GetBatchesAsync(string profile, CancellationToken ct = default)
    {
        var stored = await _batches.Find(s => s.Profile == profile).ToListAsync(ct);
        return stored.Select(s => s.Data).OrderBy(b => b.Number).ToList();
    }

    public Task SaveBatchAsync(IngestBatch batch, CancellationToken ct = default) =>
        UpsertAsync(_batches, [Stored.Of(batch.ProfileName, batch.Number.ToString(), batch, batch.Number)], ct);

    public async Task<IReadOnlyList<IngestItem>> GetItemsAsync(string profile, int? batchNumber = null, CancellationToken ct = default)
    {
        var filter = batchNumber is null
            ? Builders<Stored<IngestItem>>.Filter.Eq(s => s.Profile, profile)
            : Builders<Stored<IngestItem>>.Filter.Where(s => s.Profile == profile && s.Batch == batchNumber);
        var stored = await _items.Find(filter).ToListAsync(ct);
        return stored.Select(s => s.Data).ToList();
    }

    public Task SaveItemsAsync(IEnumerable<IngestItem> items, CancellationToken ct = default) =>
        UpsertAsync(_items, items.Select(i => Stored.Of(i.ProfileName, i.Id, i, i.BatchNumber)), ct);

    public async Task DeleteItemsAsync(IEnumerable<IngestItem> items, CancellationToken ct = default)
    {
        var ids = items.Select(i => Stored.Key(i.ProfileName, i.Id)).ToList();
        if (ids.Count > 0)
            await _items.DeleteManyAsync(s => ids.Contains(s.Id), ct);
    }

    public async Task<IReadOnlyList<TitleInfo>> GetTitlesAsync(string profile, CancellationToken ct = default)
    {
        var stored = await _titles.Find(s => s.Profile == profile).ToListAsync(ct);
        return stored.Select(s => s.Data).OrderBy(t => t.TitleKey).ToList();
    }

    public Task SaveTitlesAsync(IEnumerable<TitleInfo> titles, CancellationToken ct = default) =>
        UpsertAsync(_titles, titles.Select(t => Stored.Of(t.ProfileName, t.TitleKey, t, null)), ct);

    private static async Task UpsertAsync<T>(IMongoCollection<Stored<T>> collection, IEnumerable<Stored<T>> documents, CancellationToken ct)
    {
        var writes = documents
            .Select(d => new ReplaceOneModel<Stored<T>>(Builders<Stored<T>>.Filter.Eq(s => s.Id, d.Id), d) { IsUpsert = true })
            .ToList();
        if (writes.Count > 0)
            await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }
}

[BsonIgnoreExtraElements]
public sealed class Stored<T>
{
    [BsonId]
    public string Id { get; set; } = "";

    public string Profile { get; set; } = "";

    public int? Batch { get; set; }

    public DateTime UpdatedAt { get; set; }

    public T Data { get; set; } = default!;
}

internal static class Stored
{
    public static string Key(string profile, string key) => $"{profile}|{key}";

    public static Stored<T> Of<T>(string profile, string key, T data, int? batch) => new()
    {
        Id = Key(profile, key),
        Profile = profile,
        Batch = batch,
        UpdatedAt = DateTime.UtcNow,
        Data = data,
    };
}
