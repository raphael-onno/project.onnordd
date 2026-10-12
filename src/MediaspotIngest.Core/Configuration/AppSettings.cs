using System.Text.Json.Serialization;

namespace MediaspotIngest.Core.Configuration;

/// <summary>
/// Everything the old DaletToMediaspot tool hard-coded, now editable from the application.
/// Persisted as JSON by <see cref="SettingsStore"/>; secrets are stored encrypted (see <see cref="SecretProtector"/>).
/// </summary>
public sealed class AppSettings
{
    public StoreSettings Store { get; set; } = new();

    public MediaspotApiSettings Mediaspot { get; set; } = new();

    public List<PlatformProfile> Profiles { get; set; } = [];

    public string? ActiveProfileName { get; set; }

    [JsonIgnore]
    public PlatformProfile? ActiveProfile =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, ActiveProfileName, StringComparison.OrdinalIgnoreCase));
}

public sealed class StoreSettings
{
    /// <summary>Local keeps the state in JSON files on this machine; Mongo shares it.</summary>
    public StoreKind Kind { get; set; } = StoreKind.Local;

    public string LocalDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "onnoRDD", "data");

    /// <summary>Encrypted with DPAPI: it usually carries credentials.</summary>
    public string? ProtectedConnectionString { get; set; }

    public string Database { get; set; } = "VDMPROD";

    /// <summary>Prefix of the collections this tool owns, kept apart from the old <c>daletToMediaspot.*</c> ones.</summary>
    public string CollectionPrefix { get; set; } = "mediaspotIngest";
}

public enum StoreKind
{
    Local,
    Mongo,
}

public sealed class MediaspotApiSettings
{
    /// <summary>Services are reached as <c>{ApiBaseUrl}/{service}-service/api</c>.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.mediaspot.io";

    /// <summary>Used to build "open in Mediaspot" links: <c>{AppBaseUrl}/assets/{id}</c>.</summary>
    public string AppBaseUrl { get; set; } = "https://app.mediaspot.io";
}
