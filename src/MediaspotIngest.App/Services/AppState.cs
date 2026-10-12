using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Mediaspot;
using MediaspotIngest.Core.Persistence;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.App.Services;

/// <summary>Settings, active profile and the services built from them, shared by every screen.</summary>
public sealed partial class AppState : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private (string Path, DateTime Stamp, LegacyTemplate Template)? _template;
    private IIngestStore? _store;
    private MediaspotClient? _mediaspot;

    public AppState(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        Settings = settingsStore.Load();
    }

    public AppSettings Settings { get; private set; }

    public string SettingsFilePath => _settingsStore.FilePath;

    public PlatformProfile? Profile => Settings.ActiveProfile;

    /// <summary>Raised when the settings or the active profile changed: screens reload.</summary>
    public event EventHandler? ProfileChanged;

    [ObservableProperty]
    private string? _mediaspotUser;

    public void SaveSettings(AppSettings settings)
    {
        _settingsStore.Save(settings);
        Settings = settings;
        ResetServices();
        OnPropertyChanged(nameof(Profile));
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectProfile(string? name)
    {
        if (string.Equals(Settings.ActiveProfileName, name, StringComparison.OrdinalIgnoreCase))
            return;
        Settings.ActiveProfileName = name;
        _settingsStore.Save(Settings);
        ResetServices();
        OnPropertyChanged(nameof(Profile));
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public PlatformProfile RequireProfile() =>
        Profile ?? throw new InvalidOperationException("Aucun profil de plateforme actif : en créer un dans Configuration.");

    /// <summary>The active profile's template, re-read whenever the file changes.</summary>
    public LegacyTemplate RequireTemplate()
    {
        var path = RequireProfile().TemplatePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException($"Modèle Legacy Importer introuvable : « {path} ». Le régler dans Configuration.");

        var stamp = File.GetLastWriteTimeUtc(path);
        if (_template is { } cached && cached.Path == path && cached.Stamp == stamp)
            return cached.Template;

        var template = LegacyTemplate.Load(path);
        _template = (path, stamp, template);
        return template;
    }

    public IIngestStore Store => _store ??= CreateStore(Settings.Store);

    public static IIngestStore CreateStore(StoreSettings settings)
    {
        if (settings.Kind == StoreKind.Local)
            return new JsonFileIngestStore(settings.LocalDirectory);

        var connectionString = SecretProtector.Unprotect(settings.ProtectedConnectionString)
            ?? throw new InvalidOperationException("Chaîne de connexion MongoDB manquante : la saisir dans Configuration.");
        return new MongoIngestStore(connectionString, settings.Database, settings.CollectionPrefix);
    }

    /// <summary>Logged-in Mediaspot client for the active profile.</summary>
    public async Task<MediaspotClient> MediaspotAsync(CancellationToken ct = default)
    {
        var profile = RequireProfile();
        _mediaspot ??= new MediaspotClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, Settings.Mediaspot.ApiBaseUrl);
        if (_mediaspot.IsConnected)
            return _mediaspot;

        var password = SecretProtector.Unprotect(profile.ProtectedPassword);
        if (string.IsNullOrEmpty(profile.Login) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Identifiants Mediaspot du profil manquants : les saisir dans Configuration.");

        await _mediaspot.LoginAsync(profile.Login, password, profile.PlatformId, ct);
        MediaspotUser = _mediaspot.ConnectedUser;
        return _mediaspot;
    }

    public string AssetUrl(long assetId) => $"{Settings.Mediaspot.AppBaseUrl.TrimEnd('/')}/assets/{assetId}";

    private void ResetServices()
    {
        _template = null;
        _store = null;
        _mediaspot?.Dispose();
        _mediaspot = null;
        MediaspotUser = null;
    }
}
