using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaspotIngest.App.Services;
using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Files;
using MediaspotIngest.Core.Mediaspot;
using MediaspotIngest.Core.Persistence;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.App.ViewModels;

/// <summary>
/// Edits a working copy of the settings; nothing is applied until "Enregistrer".
/// </summary>
public sealed partial class ConfigurationViewModel : ObservableObject
{
    private readonly AppState _state;

    public ConfigurationViewModel(AppState state)
    {
        _state = state;
        Reset();
    }

    public IReadOnlyList<StoreKind> StoreKinds { get; } = Enum.GetValues<StoreKind>();

    public IReadOnlyList<FileTransferMode> TransferModes { get; } = Enum.GetValues<FileTransferMode>();

    public string SettingsFilePath => _state.SettingsFilePath;

    // --- Store ------------------------------------------------------------------------------

    [ObservableProperty] private StoreKind _storeKind;
    [ObservableProperty] private string _localDirectory = "";
    [ObservableProperty] private string _mongoDatabase = "";
    [ObservableProperty] private string _collectionPrefix = "";

    /// <summary>Typed by the user; empty means "keep the saved one".</summary>
    public string? NewMongoConnectionString { get; set; }

    public bool HasMongoConnectionString => !string.IsNullOrEmpty(_protectedConnectionString);

    private string? _protectedConnectionString;

    // --- Mediaspot ----------------------------------------------------------------------------

    [ObservableProperty] private string _apiBaseUrl = "";
    [ObservableProperty] private string _appBaseUrl = "";

    // --- Profiles -----------------------------------------------------------------------------

    public ObservableCollection<ProfileViewModel> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand), nameof(DuplicateProfileCommand))]
    private ProfileViewModel? _selectedProfile;

    [ObservableProperty]
    private string? _status;

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new ProfileViewModel(new PlatformProfile { Name = UniqueName("NOUVELLE PLATEFORME") });
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateProfile()
    {
        var copy = SelectedProfile!.ToProfile().Clone();
        copy.Name = UniqueName(copy.Name + " (copie)");
        var profile = new ProfileViewModel(copy);
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteProfile()
    {
        if (!Dialogs.Confirm($"Supprimer le profil « {SelectedProfile!.Name} » ?\nLes lots déjà enregistrés ne sont pas effacés."))
            return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    private bool HasSelection() => SelectedProfile is not null;

    [RelayCommand]
    private void Save()
    {
        var names = Profiles.Select(p => p.Name.Trim()).ToList();
        if (names.Any(string.IsNullOrEmpty) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            Dialogs.Error("Chaque profil doit avoir un nom, unique.");
            return;
        }

        var settings = BuildSettings();
        settings.ActiveProfileName = SelectedProfile?.Name ?? _state.Settings.ActiveProfileName ?? settings.Profiles.FirstOrDefault()?.Name;
        if (settings.ActiveProfile is null)
            settings.ActiveProfileName = settings.Profiles.FirstOrDefault()?.Name;

        _state.SaveSettings(settings);
        Status = $"Enregistré à {DateTime.Now:HH:mm:ss}.";
        Reset();
    }

    [RelayCommand]
    private void Cancel() => Reset();

    [RelayCommand]
    private async Task TestStoreAsync()
    {
        try
        {
            var store = AppState.CreateStore(BuildSettings().Store);
            if (store is MongoIngestStore mongo)
                await mongo.PingAsync();
            else
                Directory.CreateDirectory(LocalDirectory);
            Dialogs.Info("Stockage accessible.");
        }
        catch (Exception e)
        {
            Dialogs.Error($"Stockage inaccessible.\n\n{e.Message}");
        }
    }

    [RelayCommand]
    private async Task TestMediaspotAsync()
    {
        if (SelectedProfile is null)
            return;

        try
        {
            var profile = SelectedProfile.ToProfile();
            var password = SecretProtector.Unprotect(profile.ProtectedPassword);
            using var client = new MediaspotClient(new System.Net.Http.HttpClient(), ApiBaseUrl);
            await client.LoginAsync(profile.Login, password ?? "", profile.PlatformId);
            var fields = await client.GetAssetMetadataFieldsAsync();

            var missing = new[] { profile.StreamIdTagName, profile.CustomerOriginalFileNameTagName }
                .Where(t => !fields.ContainsKey(t))
                .ToList();
            Dialogs.Info(missing.Count == 0
                ? $"Connexion réussie ({client.ConnectedUser}), {fields.Count} métadonnées d'asset sur la plateforme."
                : $"Connexion réussie, mais ces métadonnées n'existent pas sur la plateforme : {string.Join(", ", missing)}.\nLe suivi des assets ne pourra pas les utiliser.");
        }
        catch (Exception e)
        {
            Dialogs.Error($"Connexion à Mediaspot impossible.\n\n{e.Message}");
        }
    }

    /// <summary>Lists the platform's ingest config packs and finds its Legacy Importer storage.</summary>
    [RelayCommand]
    private async Task LoadImportSettingsAsync()
    {
        if (SelectedProfile is not { } vm)
            return;

        try
        {
            var profile = vm.ToProfile();
            using var client = new MediaspotClient(new System.Net.Http.HttpClient(), ApiBaseUrl);
            await client.LoginAsync(profile.Login, SecretProtector.Unprotect(profile.ProtectedPassword) ?? "", profile.PlatformId);

            var packs = await client.GetIngestConfigPacksAsync();
            vm.ConfigPacks.Clear();
            foreach (var pack in packs)
                vm.ConfigPacks.Add(pack);
            vm.SelectedConfigPack = packs.FirstOrDefault(p => p.Id == vm.ConfigIngestPackId)
                ?? packs.FirstOrDefault(p => p.Name.Contains("RDD", StringComparison.OrdinalIgnoreCase))
                ?? (packs.Count == 1 ? packs[0] : null);

            var storages = await client.GetLegacyImporterStoragesAsync();
            vm.LegacyImporterStorageId = storages.FirstOrDefault()?.Id;

            Dialogs.Info($"{packs.Count} config pack(s) d'ingest trouvée(s)"
                + (vm.SelectedConfigPack is { } selected ? $", « {selected.Name} » présélectionnée" : " : en choisir une")
                + ".\n" + (storages.Count == 0
                    ? "Aucun stockage LegacyImporter trouvé : l'import par l'appli sera impossible."
                    : $"Stockage LegacyImporter : {storages[0]}.")
                + "\n\nPenser à « Enregistrer ».");
        }
        catch (Exception e)
        {
            Dialogs.Error($"Lecture de la configuration d'import impossible.\n\n{e.Message}");
        }
    }

    /// <summary>Fills the Aspera host and port from the provider's record in Mediaspot (login/password stay hidden on cloud platforms).</summary>
    [RelayCommand]
    private async Task FetchAsperaAsync()
    {
        if (SelectedProfile is not { } vm)
            return;

        try
        {
            var profile = vm.ToProfile();
            var password = SecretProtector.Unprotect(profile.ProtectedPassword);
            using var client = new MediaspotClient(new System.Net.Http.HttpClient(), ApiBaseUrl);
            await client.LoginAsync(profile.Login, password ?? "", profile.PlatformId);
            var aspera = await client.GetProviderAsperaAsync(profile.ProviderId);
            if (aspera is null)
            {
                Dialogs.Error($"Provider {profile.ProviderId} introuvable sur la plateforme (ou compte sans accès à la liste des providers).");
                return;
            }

            if (!string.IsNullOrWhiteSpace(aspera.Host))
                vm.AsperaHost = aspera.Host;
            if (aspera.SshPort is > 0)
                vm.AsperaSshPort = aspera.SshPort;
            if (!string.IsNullOrWhiteSpace(aspera.Login))
                vm.AsperaLogin = aspera.Login;
            vm.SetS3FromImportFolder(aspera.ImportFolder);

            Dialogs.Info($"Provider « {aspera.ProviderName} » :\n"
                + $"• hôte Aspera : {aspera.Host ?? "non renseigné"}\n"
                + $"• port SSH : {aspera.SshPort?.ToString() ?? "défaut"}\n"
                + $"• login Aspera : {aspera.Login ?? "masqué par Mediaspot (plateforme cloud) : à saisir"}\n"
                + (aspera.ImportFolder is null ? "" : $"• l'import cherchera les fichiers dans : {aspera.ImportFolder}\n")
                + "\nPenser à « Enregistrer ».");
        }
        catch (Exception e)
        {
            Dialogs.Error($"Lecture du provider impossible.\n\n{e.Message}");
        }
    }

    private AppSettings BuildSettings()
    {
        if (!string.IsNullOrWhiteSpace(NewMongoConnectionString))
            _protectedConnectionString = SecretProtector.Protect(NewMongoConnectionString.Trim());

        return new AppSettings
        {
            Store = new StoreSettings
            {
                Kind = StoreKind,
                LocalDirectory = LocalDirectory.Trim(),
                ProtectedConnectionString = _protectedConnectionString,
                Database = MongoDatabase.Trim(),
                CollectionPrefix = CollectionPrefix.Trim(),
            },
            Mediaspot = new MediaspotApiSettings { ApiBaseUrl = ApiBaseUrl.Trim(), AppBaseUrl = AppBaseUrl.Trim() },
            Profiles = Profiles.Select(p => p.ToProfile()).ToList(),
            ActiveProfileName = _state.Settings.ActiveProfileName,
        };
    }

    private void Reset()
    {
        // Deep copy through JSON so edits never touch the live settings.
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_state.Settings))!;

        StoreKind = settings.Store.Kind;
        LocalDirectory = settings.Store.LocalDirectory;
        MongoDatabase = settings.Store.Database;
        CollectionPrefix = settings.Store.CollectionPrefix;
        _protectedConnectionString = settings.Store.ProtectedConnectionString;
        NewMongoConnectionString = null;
        OnPropertyChanged(nameof(NewMongoConnectionString));
        OnPropertyChanged(nameof(HasMongoConnectionString));

        ApiBaseUrl = settings.Mediaspot.ApiBaseUrl;
        AppBaseUrl = settings.Mediaspot.AppBaseUrl;

        Profiles.Clear();
        foreach (var profile in settings.Profiles)
            Profiles.Add(new ProfileViewModel(profile));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == settings.ActiveProfileName) ?? Profiles.FirstOrDefault();
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var i = 2; Profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{name} {i}";
        return candidate;
    }
}

public sealed partial class ProfileViewModel : ObservableObject
{
    private string? _protectedPassword;

    public ProfileViewModel(PlatformProfile profile)
    {
        _name = profile.Name;
        _platformId = profile.PlatformId;
        _providerId = profile.ProviderId;
        _login = profile.Login;
        _protectedPassword = profile.ProtectedPassword;
        _sourceRootPath = profile.SourceRootPath;
        _transferTarget = profile.TransferTarget;
        _ingestDirectoryPath = profile.IngestDirectoryPath;
        _asperaHost = profile.Aspera.Host;
        _asperaSshPort = profile.Aspera.SshPort;
        _asperaLogin = profile.Aspera.Login;
        _protectedAsperaPassword = profile.Aspera.ProtectedPassword;
        _asperaRemoteDirectory = profile.Aspera.RemoteDirectory;
        _asperaTargetRate = profile.Aspera.TargetRate;
        _ascpPath = profile.Aspera.AscpPath ?? "";
        _s3Region = profile.S3.Region;
        _s3Bucket = profile.S3.Bucket;
        _s3Prefix = profile.S3.Prefix;
        _s3AccessKeyId = profile.S3.AccessKeyId;
        _protectedS3Secret = profile.S3.ProtectedSecretAccessKey;
        _doneDirectoryPath = profile.DoneDirectoryPath ?? "";
        _templatePath = profile.TemplatePath;
        _configIngestPackId = profile.ConfigIngestPackId;
        _legacyImporterStorageId = profile.LegacyImporterStorageId;
        if (profile.ConfigIngestPackId is { } packId)
        {
            var pack = new NamedId(packId, profile.ConfigIngestPackName ?? $"#{packId}");
            ConfigPacks.Add(pack);
            _selectedConfigPack = pack;
        }
        _exportDirectoryPath = profile.ExportDirectoryPath;
        _transferMode = profile.TransferMode;
        _defaultOriginalLanguage = profile.DefaultOriginalLanguage;
        _defaultPictureFormat = profile.DefaultPictureFormat;
        _streamIdTagName = profile.StreamIdTagName;
        _customerOriginalFileNameTagName = profile.CustomerOriginalFileNameTagName;
        foreach (var (tag, value) in profile.DefaultCellValues)
            DefaultCellValues.Add(new DefaultCellValue { TagName = tag, Value = value });
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private long _platformId;
    [ObservableProperty] private long _providerId;
    [ObservableProperty] private string _login;
    [ObservableProperty] private string _sourceRootPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsperaTarget), nameof(IsFolderTarget), nameof(IsS3Target))]
    private TransferTarget _transferTarget;

    [ObservableProperty] private string _s3Region;
    [ObservableProperty] private string _s3Bucket;
    [ObservableProperty] private string _s3Prefix;
    [ObservableProperty] private string _s3AccessKeyId;

    private string? _protectedS3Secret;

    public bool IsS3Target => TransferTarget == TransferTarget.S3;

    public bool HasS3Secret => !string.IsNullOrEmpty(_protectedS3Secret);

    public void SetS3Secret(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return;
        _protectedS3Secret = SecretProtector.Protect(secret);
        OnPropertyChanged(nameof(HasS3Secret));
    }

    [RelayCommand]
    private async Task TestS3Async()
    {
        try
        {
            var settings = ToProfile().S3;
            var secret = SecretProtector.Unprotect(settings.ProtectedSecretAccessKey)
                ?? throw new InvalidOperationException("Clé secrète S3 non renseignée.");
            using var s3 = new S3Uploader(settings, secret);
            Dialogs.Info(await s3.CheckAsync());
        }
        catch (Exception e)
        {
            Dialogs.Error($"S3 : {e.Message}");
        }
    }

    /// <summary>Fills bucket and prefix from the import folder Mediaspot shows for the provider.</summary>
    public void SetS3FromImportFolder(string? importFolder)
    {
        var settings = new S3Settings();
        if (!settings.TrySetFromUrl(importFolder))
            return;
        S3Bucket = settings.Bucket;
        S3Prefix = settings.Prefix;
    }

    [ObservableProperty] private string _ingestDirectoryPath;
    [ObservableProperty] private string _asperaHost;
    [ObservableProperty] private int? _asperaSshPort;
    [ObservableProperty] private string _asperaLogin;
    [ObservableProperty] private string _asperaRemoteDirectory;
    [ObservableProperty] private string _asperaTargetRate;
    [ObservableProperty] private string _ascpPath;

    private string? _protectedAsperaPassword;

    public bool IsAsperaTarget => TransferTarget == TransferTarget.Aspera;

    public bool IsFolderTarget => TransferTarget == TransferTarget.Folder;

    public IReadOnlyList<TransferTarget> TransferTargets { get; } = Enum.GetValues<TransferTarget>();

    public bool HasAsperaPassword => !string.IsNullOrEmpty(_protectedAsperaPassword);

    public string DetectedAscp => new AsperaUploader(new AsperaSettings()).AscpPath is { } path ? $"détecté : {path}" : "Aspera Connect non trouvé sur ce poste";

    public void SetAsperaPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return;
        _protectedAsperaPassword = SecretProtector.Protect(password);
        OnPropertyChanged(nameof(HasAsperaPassword));
    }

    [RelayCommand]
    private async Task TestAsperaAsync()
    {
        try
        {
            Dialogs.Info(await new AsperaUploader(ToProfile().Aspera).CheckAsync());
        }
        catch (Exception e)
        {
            Dialogs.Error($"Aspera : {e.Message}");
        }
    }
    [ObservableProperty] private string _doneDirectoryPath;
    [ObservableProperty] private string _templatePath;
    [ObservableProperty] private string _exportDirectoryPath;
    [ObservableProperty] private FileTransferMode _transferMode;
    [ObservableProperty] private string _defaultOriginalLanguage;
    [ObservableProperty] private string _defaultPictureFormat;
    [ObservableProperty] private string _streamIdTagName;
    [ObservableProperty] private string _customerOriginalFileNameTagName;

    public ObservableCollection<DefaultCellValue> DefaultCellValues { get; } = [];

    public ObservableCollection<NamedId> ConfigPacks { get; } = [];

    [ObservableProperty]
    private NamedId? _selectedConfigPack;

    [ObservableProperty]
    private long? _configIngestPackId;

    [ObservableProperty]
    private long? _legacyImporterStorageId;

    partial void OnSelectedConfigPackChanged(NamedId? value) => ConfigIngestPackId = value?.Id;

    /// <summary>Columns read from the template, to see what the platform expects.</summary>
    public ObservableCollection<TemplateColumn> TemplateColumns { get; } = [];

    [ObservableProperty]
    private string? _templateSummary;

    public bool HasPassword => !string.IsNullOrEmpty(_protectedPassword);

    /// <summary>Set from the PasswordBox; encrypted immediately.</summary>
    public void SetPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return;
        _protectedPassword = SecretProtector.Protect(password);
        OnPropertyChanged(nameof(HasPassword));
    }

    [RelayCommand]
    private void Browse(string field)
    {
        switch (field)
        {
            case nameof(TemplatePath):
                if (Dialogs.PickFile("Modèle Excel (*.xlsx)|*.xlsx", TemplatePath) is { } file)
                {
                    TemplatePath = file;
                    ReadTemplate();
                }
                break;
            case nameof(SourceRootPath):
                SourceRootPath = Dialogs.PickFolder(SourceRootPath) ?? SourceRootPath;
                break;
            case nameof(IngestDirectoryPath):
                IngestDirectoryPath = Dialogs.PickFolder(IngestDirectoryPath) ?? IngestDirectoryPath;
                break;
            case nameof(DoneDirectoryPath):
                DoneDirectoryPath = Dialogs.PickFolder(DoneDirectoryPath) ?? DoneDirectoryPath;
                break;
            case nameof(ExportDirectoryPath):
                ExportDirectoryPath = Dialogs.PickFolder(ExportDirectoryPath) ?? ExportDirectoryPath;
                break;
        }
    }

    [RelayCommand]
    private void ReadTemplate()
    {
        TemplateColumns.Clear();
        try
        {
            var template = LegacyTemplate.Load(TemplatePath);
            foreach (var column in template.DataColumns)
                TemplateColumns.Add(column);
            var mandatory = template.DataColumns.Count(c => c.Requirement is ColumnRequirement.Mandatory or ColumnRequirement.MandatoryIfSeason);
            TemplateSummary = $"{TemplateColumns.Count} colonnes, dont {mandatory} obligatoires (feuille « {template.SheetName} »).";
        }
        catch (Exception e)
        {
            TemplateSummary = $"Lecture impossible : {e.Message}";
        }
    }

    [RelayCommand]
    private void AddDefaultValue() => DefaultCellValues.Add(new DefaultCellValue());

    [RelayCommand]
    private void RemoveDefaultValue(DefaultCellValue? value)
    {
        if (value is not null)
            DefaultCellValues.Remove(value);
    }

    public PlatformProfile ToProfile() => new()
    {
        Name = Name.Trim(),
        PlatformId = PlatformId,
        ProviderId = ProviderId,
        Login = Login.Trim(),
        ProtectedPassword = _protectedPassword,
        SourceRootPath = SourceRootPath.Trim(),
        TransferTarget = TransferTarget,
        IngestDirectoryPath = IngestDirectoryPath.Trim(),
        Aspera = new AsperaSettings
        {
            Host = AsperaHost.Trim(),
            SshPort = AsperaSshPort,
            Login = AsperaLogin.Trim(),
            ProtectedPassword = _protectedAsperaPassword,
            RemoteDirectory = string.IsNullOrWhiteSpace(AsperaRemoteDirectory) ? "/temp" : AsperaRemoteDirectory.Trim(),
            TargetRate = string.IsNullOrWhiteSpace(AsperaTargetRate) ? "500m" : AsperaTargetRate.Trim(),
            AscpPath = string.IsNullOrWhiteSpace(AscpPath) ? null : AscpPath.Trim(),
        },
        S3 = new S3Settings
        {
            Region = S3Region.Trim(),
            Bucket = S3Bucket.Trim(),
            Prefix = S3Prefix.Trim(),
            AccessKeyId = S3AccessKeyId.Trim(),
            ProtectedSecretAccessKey = _protectedS3Secret,
        },
        DoneDirectoryPath = string.IsNullOrWhiteSpace(DoneDirectoryPath) ? null : DoneDirectoryPath.Trim(),
        TemplatePath = TemplatePath.Trim(),
        ConfigIngestPackId = SelectedConfigPack?.Id,
        ConfigIngestPackName = SelectedConfigPack?.Name,
        LegacyImporterStorageId = LegacyImporterStorageId,
        ExportDirectoryPath = ExportDirectoryPath.Trim(),
        TransferMode = TransferMode,
        DefaultOriginalLanguage = DefaultOriginalLanguage.Trim(),
        DefaultPictureFormat = DefaultPictureFormat.Trim(),
        StreamIdTagName = StreamIdTagName.Trim(),
        CustomerOriginalFileNameTagName = CustomerOriginalFileNameTagName.Trim(),
        DefaultCellValues = DefaultCellValues
            .Where(d => !string.IsNullOrWhiteSpace(d.TagName))
            .GroupBy(d => d.TagName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase),
    };
}

public sealed partial class DefaultCellValue : ObservableObject
{
    [ObservableProperty] private string _tagName = "";
    [ObservableProperty] private string _value = "";
}
