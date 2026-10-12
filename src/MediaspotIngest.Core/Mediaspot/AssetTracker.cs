using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.Mediaspot;

/// <summary>
/// After the user ran the legacy import in Mediaspot, finds the asset each item became and reads its status
/// (the old tool's "Update GraphIds"): by streamId, which we set to the registry id, else by the client file name.
/// </summary>
public sealed class AssetTracker(MediaspotClient client, PlatformProfile profile)
{
    public async Task<TrackingResult> RefreshAsync(IngestItem item, CancellationToken ct = default)
    {
        var matches = await client.FindAssetsByMetadataAsync(profile.StreamIdTagName, item.Id, ct);
        if (matches.Count == 0)
            matches = await client.FindAssetsByMetadataAsync(profile.CustomerOriginalFileNameTagName, item.FileName, ct);

        switch (matches.Count)
        {
            case 0:
                item.MediaspotAssetId = null;
                item.MediaspotStatus = null;
                return TrackingResult.NotFound;
            case 1:
                item.MediaspotAssetId = matches[0].Id;
                item.MediaspotStatus = matches[0].Status;
                return TrackingResult.Found;
            default:
                // Several live assets for one file: never guess, let the user look.
                item.MediaspotAssetId = null;
                item.MediaspotStatus = $"{matches.Count} assets : {string.Join(", ", matches.Select(m => m.Id))}";
                return TrackingResult.Ambiguous;
        }
    }
}

public enum TrackingResult
{
    NotFound,
    Found,
    Ambiguous,
}
