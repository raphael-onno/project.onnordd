using System.Text.Json;
using System.Text.Json.Serialization;
using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.Persistence;

/// <summary>
/// Local store: one folder per profile, holding batches.json, titles.json and items.json.
/// For working alone or without database access; switch to Mongo to share the state.
/// </summary>
public sealed class JsonFileIngestStore(string rootDirectory) : IIngestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task<IReadOnlyList<IngestBatch>> GetBatchesAsync(string profile, CancellationToken ct = default) =>
        LockedAsync(() => (IReadOnlyList<IngestBatch>)Read<IngestBatch>(profile, "batches").OrderBy(b => b.Number).ToList(), ct);

    public Task SaveBatchAsync(IngestBatch batch, CancellationToken ct = default) =>
        LockedAsync(() => Upsert(batch.ProfileName, "batches", [batch], b => b.Number.ToString()), ct);

    public Task<IReadOnlyList<IngestItem>> GetItemsAsync(string profile, int? batchNumber = null, CancellationToken ct = default) =>
        LockedAsync(() => (IReadOnlyList<IngestItem>)Read<IngestItem>(profile, "items")
            .Where(i => batchNumber is null || i.BatchNumber == batchNumber)
            .ToList(), ct);

    public Task SaveItemsAsync(IEnumerable<IngestItem> items, CancellationToken ct = default) =>
        LockedAsync(() =>
        {
            foreach (var group in items.GroupBy(i => i.ProfileName))
                Upsert(group.Key, "items", group.ToList(), i => i.Id);
        }, ct);

    public Task DeleteItemsAsync(IEnumerable<IngestItem> items, CancellationToken ct = default) =>
        LockedAsync(() =>
        {
            foreach (var group in items.GroupBy(i => i.ProfileName))
            {
                var ids = group.Select(i => i.Id).ToHashSet();
                var kept = Read<IngestItem>(group.Key, "items").Where(i => !ids.Contains(i.Id)).ToList();
                Write(group.Key, "items", kept);
            }
        }, ct);

    public Task<IReadOnlyList<TitleInfo>> GetTitlesAsync(string profile, CancellationToken ct = default) =>
        LockedAsync(() => (IReadOnlyList<TitleInfo>)Read<TitleInfo>(profile, "titles").OrderBy(t => t.TitleKey).ToList(), ct);

    public Task SaveTitlesAsync(IEnumerable<TitleInfo> titles, CancellationToken ct = default) =>
        LockedAsync(() =>
        {
            foreach (var group in titles.GroupBy(t => t.ProfileName))
                Upsert(group.Key, "titles", group.ToList(), t => t.TitleKey);
        }, ct);

    private void Upsert<T>(string profile, string name, IReadOnlyCollection<T> documents, Func<T, string> key)
    {
        var byKey = Read<T>(profile, name).ToDictionary(key);
        foreach (var document in documents)
            byKey[key(document)] = document;
        Write(profile, name, byKey.Values.ToList());
    }

    private List<T> Read<T>(string profile, string name)
    {
        var path = FilePath(profile, name);
        if (!File.Exists(path))
            return [];
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<T>>(stream, JsonOptions) ?? [];
    }

    private void Write<T>(string profile, string name, List<T> documents)
    {
        var path = FilePath(profile, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(documents, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private string FilePath(string profile, string name)
    {
        var folder = string.Concat(profile.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(rootDirectory, folder, name + ".json");
    }

    private async Task<T> LockedAsync<T>(Func<T> action, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await Task.Run(action, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task LockedAsync(Action action, CancellationToken ct) =>
        LockedAsync(() =>
        {
            action();
            return true;
        }, ct);
}
