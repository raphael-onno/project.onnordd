using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaspotIngest.App.Services;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Mediaspot;

namespace MediaspotIngest.App.ViewModels;

/// <summary>
/// "Importer dans Mediaspot": the Legacy Importer steps of the NRJ12 tutorial, driven from the tool —
/// upload, Check assets, Import assets, then the request ids read back (the old "Update GraphIds").
/// </summary>
public sealed partial class LegacyImportViewModel(AppState state, string batchName, IReadOnlyList<IngestItem> items) : ObservableObject
{
    private LegacyImportSession? _session;

    public IReadOnlyList<IngestItem> Items { get; } = items;

    public ObservableCollection<RowReport> Reports { get; } = [];

    public string Intro =>
        $"{Items.Count} ligne(s) prête(s) de {batchName}, fichiers déjà dans le dossier temp du provider. "
        + $"Provider #{state.Profile?.ProviderId}, config pack « {state.Profile?.ConfigIngestPackName ?? "non choisie"} ».";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _checkPassed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(ImportCommand))]
    private bool _imported;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private double _progress;

    /// <summary>True once items were changed (request ids), so the batch screen saves and refreshes them.</summary>
    public bool ItemsChanged { get; private set; }

    private bool CanCheck() => !IsBusy && !Imported;

    private bool CanImport() => !IsBusy && CheckPassed && !Imported;

    /// <summary>Writes and uploads the XLSX (first time), then asks Mediaspot to check every row. Creates nothing.</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckAsync() => RunAsync(async () =>
    {
        var client = await state.MediaspotAsync();
        if (_session is null)
        {
            var profile = state.RequireProfile();
            Status = "Génération du XLSX…";
            var directory = string.IsNullOrWhiteSpace(profile.ExportDirectoryPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MediaspotIngest")
                : profile.ExportDirectoryPath;
            _session = await LegacyImportSession.PrepareAsync(client, profile, state.RequireTemplate(), Items, directory,
                $"{profile.Name}_{batchName}_{DateTime.Now:yyyyMMdd_HHmmss}");

            Status = $"Envoi de {_session.FileName} sur Mediaspot…";
            await _session.UploadAsync();
        }

        Status = "Check assets…";
        var reports = await _session.CheckAsync(new Progress<double>(p => Progress = p * 100));
        Show(reports);
        var errors = reports.Count(r => r.IsError);
        CheckPassed = errors == 0;
        Status = CheckPassed
            ? "Contrôle OK : rien n'est encore créé. « Importer » crée les requests et lance le transfert des fichiers."
            : $"{errors} erreur(s) : corriger dans l'appli (ou déposer les fichiers manquants dans temp), puis recontrôler.";
    });

    /// <summary>"Import assets": creates the requests and moves/uploads the files under their request id.</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (!Dialogs.Confirm($"Importer {Items.Count} asset(s) dans Mediaspot ?\n\nMediaspot va créer les requests, renommer chaque fichier avec son numéro de request et le transférer vers le stockage d'ingest. "
            + $"Cela consomme {(Items.Count + LegacyImportSession.MaxRowsPerCall - 1) / LegacyImportSession.MaxRowsPerCall} jeton(s) d'import de la plateforme."))
            return;

        await RunAsync(async () =>
        {
            Status = "Import assets…";
            var reports = await _session!.ImportAsync(new Progress<double>(p => Progress = p * 100));
            Show(reports);
            Imported = true;

            Status = "Lecture des numéros de request…";
            var applied = await _session.ReadBackAssetIdsAsync();
            ItemsChanged = applied > 0;
            await state.Store.SaveItemsAsync(Items);

            var errors = reports.Count(r => r.IsError);
            Status = $"Import terminé : {applied}/{Items.Count} request(s) créée(s)"
                + (errors > 0 ? $", {errors} erreur(s) (voir la liste)." : ".")
                + " L'ingest se poursuit dans Mediaspot : suivre l'avancement avec « Suivi Mediaspot ».";
        });
    }

    [RelayCommand]
    private void OpenXlsx()
    {
        if (_session is not null)
            Dialogs.ShowInExplorer(_session.LocalPath);
    }

    private void Show(IReadOnlyList<RowReport> reports)
    {
        Reports.Clear();
        foreach (var report in reports.OrderByDescending(r => r.IsError).ThenBy(r => r.Report.Row))
            Reports.Add(report);
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        Progress = 0;
        try
        {
            await action();
        }
        catch (Exception e)
        {
            Status = e.Message;
            Dialogs.Error(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
