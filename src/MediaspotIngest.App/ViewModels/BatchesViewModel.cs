using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaspotIngest.App.Services;
using MediaspotIngest.App.Views;
using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Export;
using MediaspotIngest.Core.Files;
using MediaspotIngest.Core.Filling;
using MediaspotIngest.Core.Mediaspot;
using MediaspotIngest.Core.Template;
using MediaspotIngest.Core.Validation;

namespace MediaspotIngest.App.ViewModels;

public sealed partial class BatchesViewModel : ObservableObject
{
    private readonly AppState _state;
    private LegacyTemplate? _template;
    private RowValidator? _validator;
    private CancellationTokenSource? _saveDelay;

    public BatchesViewModel(AppState state)
    {
        _state = state;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = o => o is ItemRow row && Matches(row);
    }

    public ObservableCollection<IngestBatch> Batches { get; } = [];

    public ObservableCollection<ItemRow> Items { get; } = [];

    public ICollectionView ItemsView { get; }

    public ObservableCollection<CellRow> Cells { get; } = [];

    public IReadOnlyList<string> StatusFilters { get; } = ["Tous", .. Enum.GetNames<ItemStatus>()];

    [ObservableProperty]
    private IngestBatch? _selectedBatch;

    [ObservableProperty]
    private ItemRow? _selectedItem;

    /// <summary>Rows selected in the grid (multi-selection), set from the view.</summary>
    public IReadOnlyList<ItemRow> SelectedItems { get; set; } = [];

    [ObservableProperty]
    private string _statusFilter = "Tous";

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    public bool IsIdle => !IsBusy;

    partial void OnSearchChanged(string value) => ItemsView.Refresh();

    partial void OnStatusFilterChanged(string value) => ItemsView.Refresh();

    partial void OnSelectedBatchChanged(IngestBatch? value) => _ = LoadItemsAsync();

    partial void OnSelectedItemChanged(ItemRow? value) => ShowCells(value);

    public async Task ReloadAsync()
    {
        Batches.Clear();
        Items.Clear();
        Cells.Clear();
        _template = null;
        _validator = null;
        if (_state.Profile is not { } profile)
        {
            Status = "Aucun profil actif.";
            return;
        }

        try
        {
            _template = _state.RequireTemplate();
            _validator = new RowValidator(_template);
        }
        catch (Exception e)
        {
            Status = e.Message;
        }

        try
        {
            foreach (var batch in await _state.Store.GetBatchesAsync(profile.Name))
                Batches.Add(batch);
            SelectedBatch = Batches.LastOrDefault();
            if (Batches.Count == 0)
                Status ??= "Aucun lot : en créer un depuis l'export du registre.";
        }
        catch (Exception e)
        {
            Status = $"Stockage inaccessible : {e.Message}";
        }
    }

    private async Task LoadItemsAsync()
    {
        Items.Clear();
        Cells.Clear();
        if (SelectedBatch is null || _state.Profile is not { } profile)
        {
            UpdateSummary();
            return;
        }

        var items = await _state.Store.GetItemsAsync(profile.Name, SelectedBatch.Number);
        _titles = (await _state.Store.GetTitlesAsync(profile.Name)).ToDictionary(t => t.TitleKey);
        foreach (var item in items.OrderBy(i => i.TitleKey).ThenBy(i => i.RelativePath))
        {
            var row = new ItemRow(item);
            row.Evaluate(_validator);
            Items.Add(row);
        }

        UpdateSummary();
        // Files appear on the Qumulo as restores complete: look for them every time a batch opens.
        await LocateQuietlyAsync();
    }

    /// <summary>Reloads the current batch (after titles changed on the Titles page).</summary>
    public Task RefreshAsync() => LoadItemsAsync();

    private async Task LocateQuietlyAsync()
    {
        if (IsBusy || _state.Profile is not { } profile || Items.Count == 0)
            return;

        var files = new MediaFiles(profile);
        var rows = Items.ToList();
        var changed = new List<IngestItem>();
        foreach (var row in rows)
        {
            var before = (row.Item.Location, row.Item.ActualSizeBytes, row.Item.FileWarning);
            await Task.Run(() => files.Locate(row.Item));
            if (before != (row.Item.Location, row.Item.ActualSizeBytes, row.Item.FileWarning))
                changed.Add(row.Item);
            row.Evaluate(_validator);
        }

        if (changed.Count > 0)
            await _state.Store.SaveItemsAsync(changed);
        UpdateSummary();
    }

    // --- Guided steps ------------------------------------------------------------------------

    private Dictionary<string, TitleInfo> _titles = [];

    /// <summary>Set by the main window: opens the Titles page.</summary>
    public Action? OpenTitles { get; init; }

    [ObservableProperty] private int _titlesToCreate;
    [ObservableProperty] private int _titlesIncomplete;
    [ObservableProperty] private int _rowsToFix;
    [ObservableProperty] private int _filesMissing;
    [ObservableProperty] private int _filesToSend;
    [ObservableProperty] private int _readyToImport;
    [ObservableProperty] private int _inProgress;
    [ObservableProperty] private int _done;

    public bool HasBatch => SelectedBatch is not null && Items.Count > 0;

    public string TitlesStepText => TitlesToCreate == 0
        ? "Toutes les œuvres existent dans Mediaspot."
        : TitlesIncomplete > 0
            ? $"{TitlesToCreate} œuvre(s) à créer, dont {TitlesIncomplete} à compléter (titre, année)."
            : $"{TitlesToCreate} œuvre(s) à créer dans Mediaspot.";

    public string FilesStepText => FilesToSend > 0
        ? $"{FilesToSend} fichier(s) prêt(s) à envoyer" + (FilesMissing > 0 ? $", {FilesMissing} pas encore restauré(s)." : ".")
        : FilesMissing > 0 ? $"{FilesMissing} fichier(s) pas encore restauré(s) sur le Qumulo." : "Tous les fichiers sont dans le dossier temp.";

    public string ImportStepText => ReadyToImport > 0
        ? $"{ReadyToImport} ligne(s) prête(s) à importer."
        : RowsToFix > 0 ? $"{RowsToFix} ligne(s) à corriger avant import." : "Rien à importer pour l'instant.";

    public string TrackingStepText => InProgress + Done == 0
        ? "Aucune request créée."
        : $"{InProgress} en cours, {Done} terminée(s).";

    /// <summary>Creates the batch's titles in Mediaspot, or sends to the Titles page when some are incomplete.</summary>
    [RelayCommand]
    private async Task CreateTitlesAsync()
    {
        var profile = _state.RequireProfile();
        var missing = Items.Select(r => r.TitleKey).Distinct()
            .Select(k => _titles.GetValueOrDefault(k))
            .Where(t => t is not null && t.VdmTitleId is null)
            .Cast<TitleInfo>()
            .ToList();
        if (missing.Count == 0)
        {
            Status = "Toutes les œuvres du lot existent déjà dans Mediaspot.";
            return;
        }

        var incomplete = missing.Where(t => TitleCreator.Check(t).Count > 0).ToList();
        if (incomplete.Count > 0)
        {
            if (Dialogs.Confirm($"{incomplete.Count} œuvre(s) à compléter avant création (titre et année) :\n{string.Join("\n", incomplete.Take(8).Select(t => "• " + t.TitleKey))}\n\nOuvrir la page Titres ?"))
                OpenTitles?.Invoke();
            return;
        }

        if (!Dialogs.Confirm($"Créer {missing.Count} œuvre(s) dans Mediaspot ?\nUne œuvre qui existe déjà (même Title ID, ou même nom) est réutilisée."))
            return;

        await RunBusyAsync("Création des œuvres…", async () =>
        {
            var creator = new TitleCreator(await _state.MediaspotAsync());
            var failures = new List<string>();
            for (var i = 0; i < missing.Count; i++)
            {
                var title = missing[i];
                var result = await creator.CreateOrFindAsync(title, profile.DefaultOriginalLanguage, profile.DefaultPictureFormat);
                if (result.TitleId is { } id)
                    title.VdmTitleId = id;
                else
                    failures.Add($"{title.TitleKey} : {result.Error}");
                Progress = (i + 1) * 100.0 / missing.Count;
            }

            await _state.Store.SaveTitlesAsync(missing);
            var builder = new ItemRowBuilder(_state.RequireTemplate(), profile);
            foreach (var row in Items)
            {
                if (_titles.TryGetValue(row.TitleKey, out var title))
                    builder.FillTitle(row.Item, title);
                row.Evaluate(_validator);
            }

            await _state.Store.SaveItemsAsync(Items.Select(r => r.Item));
            ShowCells(SelectedItem);
            Status = failures.Count == 0
                ? $"{missing.Count} œuvre(s) prête(s) dans Mediaspot."
                : $"{failures.Count} échec(s) : {string.Join(" ; ", failures.Take(3))}";
        });
    }

    /// <summary>Shows only the rows that still need a fix.</summary>
    [RelayCommand]
    private void ShowRowsToFix()
    {
        StatusFilter = nameof(ItemStatus.FixNeeded);
        SelectedItem = ItemsView.Cast<ItemRow>().FirstOrDefault();
    }

    [RelayCommand]
    private void ShowAll() => StatusFilter = "Tous";

    [RelayCommand]
    private void OpenTitlesPage() => OpenTitles?.Invoke();

    /// <summary>Looks for the files again, then sends those found on the Qumulo to the provider's temp folder.</summary>
    [RelayCommand]
    private async Task SendFilesAsync()
    {
        await LocateQuietlyAsync();
        await TransferToIngestAsync();
    }

    // --- Creating a batch -------------------------------------------------------------------

    [RelayCommand]
    private async Task NewBatchFromRegistryAsync()
    {
        var profile = _state.RequireProfile();
        var template = _state.RequireTemplate();

        var existing = await _state.Store.GetItemsAsync(profile.Name);
        var dialogModel = new ImportRegistryViewModel(existing.Select(i => i.Id).ToHashSet());
        var dialog = new ImportRegistryWindow { DataContext = dialogModel, Owner = Dialogs.Owner() };
        if (dialog.ShowDialog() != true)
            return;

        var elements = dialogModel.SelectedElements();
        if (elements.Count == 0)
            return;

        await RunBusyAsync("Création du lot…", async () =>
        {
            var number = Batches.Count == 0 ? 1 : Batches.Max(b => b.Number) + 1;
            var batch = new IngestBatch { ProfileName = profile.Name, Number = number, Description = dialogModel.Description };

            var titles = (await _state.Store.GetTitlesAsync(profile.Name)).ToDictionary(t => t.TitleKey);
            var newTitles = elements
                .Where(e => !titles.ContainsKey(e.TitleKey))
                .GroupBy(e => e.TitleKey)
                .Select(g => new TitleInfo { ProfileName = profile.Name, TitleKey = g.Key, MainTitle = g.First().TitleName })
                .ToList();
            foreach (var title in newTitles)
                titles[title.TitleKey] = title;

            var builder = new ItemRowBuilder(template, profile);
            var files = new MediaFiles(profile);
            var items = new List<IngestItem>();
            for (var i = 0; i < elements.Count; i++)
            {
                var item = IngestItem.FromRegistry(elements[i], profile.Name, number);
                builder.Fill(item, titles[item.TitleKey]);
                await Task.Run(() => files.Locate(item));
                items.Add(item);
                Progress = (i + 1) * 100.0 / elements.Count;
            }

            await _state.Store.SaveTitlesAsync(newTitles);
            await _state.Store.SaveBatchAsync(batch);
            await _state.Store.SaveItemsAsync(items);

            Batches.Add(batch);
            SelectedBatch = batch;
            Status = $"{batch.Name} créé : {items.Count} items, {newTitles.Count} nouveau(x) titre(s) à compléter dans l'onglet Titres.";
        });
    }

    // --- Actions on items --------------------------------------------------------------------

    [RelayCommand]
    private Task LocateFilesAsync() => RunBusyAsync("Recherche des fichiers…", async () =>
    {
        var profile = _state.RequireProfile();
        var files = new MediaFiles(profile);
        var rows = Targets();
        for (var i = 0; i < rows.Count; i++)
        {
            await Task.Run(() => files.Locate(rows[i].Item));
            rows[i].Evaluate(_validator);
            Progress = (i + 1) * 100.0 / rows.Count;
        }

        await _state.Store.SaveItemsAsync(rows.Select(r => r.Item));
        var missing = rows.Count(r => r.Location == FileLocation.NotFound);
        Status = missing == 0 ? $"{rows.Count} fichier(s) trouvé(s)." : $"{missing} fichier(s) introuvable(s) sur {rows.Count}.";
    });

    [RelayCommand]
    private async Task RefillAsync()
    {
        var rows = Targets();
        if (!Dialogs.Confirm($"Recalculer toutes les colonnes de {rows.Count} item(s) depuis le registre et les titres ?\nLes valeurs saisies à la main sur ces items seront remplacées."))
            return;

        await RunBusyAsync("Pré-remplissage…", async () =>
        {
            var profile = _state.RequireProfile();
            var titles = (await _state.Store.GetTitlesAsync(profile.Name)).ToDictionary(t => t.TitleKey);
            var builder = new ItemRowBuilder(_state.RequireTemplate(), profile);
            foreach (var row in rows)
            {
                builder.Fill(row.Item, titles.GetValueOrDefault(row.TitleKey));
                row.Evaluate(_validator);
            }

            await _state.Store.SaveItemsAsync(rows.Select(r => r.Item));
            ShowCells(SelectedItem);
            Status = $"{rows.Count} item(s) pré-rempli(s).";
        });
    }

    [RelayCommand]
    private async Task TransferToIngestAsync()
    {
        var profile = _state.RequireProfile();
        string? secret = null;
        string destination;
        switch (profile.TransferTarget)
        {
            case TransferTarget.Aspera:
                secret = SecretProtector.Unprotect(profile.Aspera.ProtectedPassword);
                if (string.IsNullOrWhiteSpace(profile.Aspera.Host) || string.IsNullOrWhiteSpace(profile.Aspera.Login) || secret is null)
                {
                    Dialogs.Error("Compte Aspera incomplet (hôte, login, mot de passe) : le renseigner dans Configuration.");
                    return;
                }

                destination = $"par Aspera vers {profile.Aspera.Login}@{profile.Aspera.Host}:{profile.Aspera.RemoteDirectory}";
                break;

            case TransferTarget.S3:
                secret = SecretProtector.Unprotect(profile.S3.ProtectedSecretAccessKey);
                if (string.IsNullOrWhiteSpace(profile.S3.Bucket) || string.IsNullOrWhiteSpace(profile.S3.Region)
                    || string.IsNullOrWhiteSpace(profile.S3.AccessKeyId) || secret is null)
                {
                    Dialogs.Error("Accès S3 incomplet (région, bucket, clé d'accès) : le renseigner dans Configuration.");
                    return;
                }

                destination = $"vers s3://{profile.S3.Bucket}/{profile.S3.KeyFor("")}";
                break;

            default:
                destination = $"vers le dossier d'ingest {profile.IngestDirectoryPath}";
                break;
        }

        var plan = TransferPlan.ToIngest(profile, Targets().Select(r => r.Item), secret);
        if (plan.Steps.Count == 0)
        {
            Dialogs.Info("Aucun fichier à transférer : les items doivent être trouvés dans la zone de restauration (lancer « Localiser »).");
            return;
        }

        await ConfirmAndRunAsync(plan, destination);
    }

    [RelayCommand]
    private async Task MoveToDoneAsync()
    {
        var profile = _state.RequireProfile();
        if (profile.TransferTarget != TransferTarget.Folder)
        {
            Dialogs.Info("Le dossier temp du provider est distant (Aspera ou S3) : c'est Mediaspot qui en sort les fichiers à l'import, rien à faire ici.");
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.DoneDirectoryPath))
        {
            Dialogs.Info("Aucun dossier « done » configuré pour ce profil.");
            return;
        }

        var accepted = Targets().Where(r => r.Status == ItemStatus.Done).Select(r => r.Item);
        var plan = TransferPlan.ToDone(profile, accepted);
        if (plan.Steps.Count == 0)
        {
            Dialogs.Info("Aucun item accepté par Mediaspot dont le fichier est encore dans le dossier d'ingest.");
            return;
        }

        await ConfirmAndRunAsync(plan, $"vers le dossier done {profile.DoneDirectoryPath}");
    }

    private async Task ConfirmAndRunAsync(TransferPlan plan, string destination)
    {
        var dialog = new TransferPlanWindow { DataContext = plan, Owner = Dialogs.Owner(), Title = $"Transfert {destination}" };
        if (dialog.ShowDialog() != true)
            return;

        await RunBusyAsync("Transfert des fichiers…", async () =>
        {
            var progress = new Progress<TransferProgress>(p =>
            {
                Progress = p.Total == 0 ? 100 : (p.Done + p.FileFraction) * 100.0 / p.Total;
                Status = p.Current is null
                    ? "Transfert terminé."
                    : $"Transfert {p.Done + 1}/{p.Total} ({p.FileFraction:P0}) : {p.Current}";
            });
            var results = await plan.RunAsync(progress, CancellationToken.None);

            var files = new MediaFiles(_state.RequireProfile());
            var rows = Items.Where(r => results.Any(s => s.Item == r.Item)).ToList();
            foreach (var row in rows)
            {
                files.Locate(row.Item);
                row.Evaluate(_validator);
            }

            await _state.Store.SaveItemsAsync(rows.Select(r => r.Item));
            var failures = results.Where(r => r.Problem is not null).ToList();
            Status = failures.Count == 0
                ? $"{results.Count} élément(s) transféré(s) {destination}."
                : $"{failures.Count} échec(s) sur {results.Count} : {string.Join(" ; ", failures.Take(3).Select(f => $"{f.Item.FileName} ({f.Problem})"))}";
        });
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var profile = _state.RequireProfile();
        var template = _state.RequireTemplate();
        var rows = Targets();
        var ready = rows.Where(r => r.Status is ItemStatus.Ready or ItemStatus.Exported).ToList();
        if (ready.Count == 0)
        {
            Dialogs.Info("Aucun item prêt (statut Ready) à exporter.");
            return;
        }

        var notInIngest = ready.Count(r => r.Location != FileLocation.Ingest);
        var message = $"Exporter {ready.Count} item(s) dans un fichier Legacy Importer ?";
        if (rows.Count > ready.Count)
            message += $"\n{rows.Count - ready.Count} item(s) non prêt(s) sont laissés de côté.";
        if (notInIngest > 0)
            message += $"\n\n⚠ {notInIngest} fichier(s) ne sont pas encore dans le dossier d'ingest : les transférer avant de lancer l'import dans Mediaspot.";
        if (!Dialogs.Confirm(message))
            return;

        var directory = string.IsNullOrWhiteSpace(profile.ExportDirectoryPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MediaspotIngest")
            : profile.ExportDirectoryPath;
        var batchName = SelectedBatch?.Name ?? "Batch";
        var path = LegacyImporterWriter.Write(template, ready.Select(r => r.Item).ToList(), directory,
            $"{profile.Name}_{batchName}_{DateTime.Now:yyyyMMdd_HHmmss}");

        foreach (var row in ready)
        {
            row.Item.ExportedAt = DateTime.Now;
            row.Evaluate(_validator);
        }

        await _state.Store.SaveItemsAsync(ready.Select(r => r.Item));
        UpdateSummary();
        Status = $"Exporté : {path}";
        if (Dialogs.Confirm($"Fichier créé :\n{path}\n\nÀ importer dans Mediaspot via le Legacy Importer.\nOuvrir le dossier ?"))
            Dialogs.ShowInExplorer(path);
    }

    /// <summary>Opens the Legacy Importer run (upload, Check assets, Import assets) on the ready rows already in temp.</summary>
    [RelayCommand]
    private async Task ImportInMediaspotAsync()
    {
        var profile = _state.RequireProfile();
        _state.RequireTemplate();
        var rows = Targets();
        var importable = rows
            .Where(r => r.Status is ItemStatus.Ready or ItemStatus.Exported && r.Location == FileLocation.Ingest && r.Item.MediaspotAssetId is null)
            .ToList();
        if (importable.Count == 0)
        {
            Dialogs.Info("Aucune ligne à importer : il faut des lignes « Ready » dont le fichier est déjà dans le dossier temp du provider (« Transférer vers l'ingest »), et pas encore importées.");
            return;
        }

        if (profile.ConfigIngestPackId is null)
        {
            Dialogs.Error("Choisir d'abord l'ingest config pack du profil (Configuration → 4 → Charger depuis Mediaspot).");
            return;
        }

        var skipped = rows.Count - importable.Count;
        if (skipped > 0 && !Dialogs.Confirm($"{importable.Count} ligne(s) importable(s), {skipped} laissée(s) de côté (pas prêtes, fichier pas encore dans temp, ou déjà importées). Continuer ?"))
            return;

        var model = new LegacyImportViewModel(_state, SelectedBatch?.Name ?? "Batch", importable.Select(r => r.Item).ToList());
        new LegacyImportWindow { DataContext = model, Owner = Dialogs.Owner() }.ShowDialog();

        if (model.ItemsChanged)
        {
            foreach (var row in importable)
                row.Evaluate(_validator);
            UpdateSummary();
        }
    }

    [RelayCommand]
    private Task TrackInMediaspotAsync() => RunBusyAsync("Suivi Mediaspot…", async () =>
    {
        var profile = _state.RequireProfile();
        var client = await _state.MediaspotAsync();
        var tracker = new AssetTracker(client, profile);
        var rows = Targets().Where(r => r.Item.ExportedAt is not null || r.Item.MediaspotAssetId is not null).ToList();
        if (rows.Count == 0)
        {
            Status = "Aucun item exporté à suivre.";
            return;
        }

        var found = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (await tracker.RefreshAsync(rows[i].Item) == TrackingResult.Found)
                found++;
            rows[i].Evaluate(_validator);
            Progress = (i + 1) * 100.0 / rows.Count;
        }

        await _state.Store.SaveItemsAsync(rows.Select(r => r.Item));
        UpdateSummary();
        Status = $"{found}/{rows.Count} item(s) retrouvé(s) dans Mediaspot.";
    });

    /// <summary>The old "Export Recap XLSX": every batch of the profile, one row per element, plus a summary per batch.</summary>
    [RelayCommand]
    private Task ExportRecapAsync() => RunBusyAsync("Récapitulatif…", async () =>
    {
        var profile = _state.RequireProfile();
        var items = await _state.Store.GetItemsAsync(profile.Name);
        var directory = string.IsNullOrWhiteSpace(profile.ExportDirectoryPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MediaspotIngest")
            : profile.ExportDirectoryPath;
        var path = await Task.Run(() => RecapWriter.Write(profile.Name, items, _template, directory));
        Status = $"Récapitulatif : {path}";
        if (Dialogs.Confirm($"Récapitulatif de {items.Count} élément(s) créé :\n{path}\n\nLes statuts Mediaspot sont ceux du dernier « Suivi Mediaspot ».\nOuvrir le dossier ?"))
            Dialogs.ShowInExplorer(path);
    });

    [RelayCommand]
    private void OpenInMediaspot()
    {
        if (SelectedItem?.MediaspotAssetId is { } id)
            Dialogs.Open(_state.AssetUrl(id));
    }

    [RelayCommand]
    private async Task RemoveFromBatchAsync()
    {
        var rows = SelectedItems.ToList();
        if (rows.Count == 0 || !Dialogs.Confirm($"Retirer {rows.Count} item(s) du lot ?\nAucun fichier n'est touché ; ils pourront être réimportés dans un autre lot."))
            return;

        await _state.Store.DeleteItemsAsync(rows.Select(r => r.Item));
        foreach (var row in rows)
            Items.Remove(row);
        UpdateSummary();
    }

    // --- Cell editing ------------------------------------------------------------------------

    private void ShowCells(ItemRow? row)
    {
        Cells.Clear();
        if (row is null || _template is null)
            return;

        var issues = row.Issues.ToDictionary(i => i.Column.Index, i => i.Message);
        foreach (var column in _template.DataColumns)
            Cells.Add(new CellRow(column, row.Item.GetCell(column.TagName), issues.GetValueOrDefault(column.Index), OnCellChanged));
    }

    private void OnCellChanged(CellRow cell)
    {
        if (SelectedItem is not { } row)
            return;

        row.Item.SetCell(cell.TagName, cell.Value);
        row.Evaluate(_validator);
        var issues = row.Issues.ToDictionary(i => i.Column.Index, i => i.Message);
        foreach (var c in Cells)
            c.Issue = issues.GetValueOrDefault(c.Column.Index);
        UpdateSummary();
        ScheduleSave(row.Item);
    }

    /// <summary>Edits are saved half a second after the last keystroke: nothing to remember to save.</summary>
    private void ScheduleSave(IngestItem item)
    {
        _saveDelay?.Cancel();
        _saveDelay = new CancellationTokenSource();
        var token = _saveDelay.Token;
        _ = Task.Delay(500, token).ContinueWith(async t =>
        {
            if (t.IsCanceled)
                return;
            try
            {
                await _state.Store.SaveItemsAsync([item]);
            }
            catch (Exception e)
            {
                Status = $"Enregistrement impossible : {e.Message}";
            }
        }, TaskScheduler.Default);
    }

    // --- Helpers -----------------------------------------------------------------------------

    /// <summary>
    /// Actions work on the whole batch. Selecting several rows (Ctrl/Maj + clic) limits them to those rows;
    /// a single selected row is just the one shown on the right.
    /// </summary>
    private List<ItemRow> Targets() =>
        SelectedItems.Count > 1 ? SelectedItems.ToList() : Items.ToList();

    private bool Matches(ItemRow row)
    {
        if (StatusFilter != "Tous" && row.Status.ToString() != StatusFilter)
            return false;
        return string.IsNullOrWhiteSpace(Search)
            || row.RelativePath.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || row.TitleKey.Contains(Search, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSummary()
    {
        var total = Items.Sum(i => i.Item.ActualSizeBytes ?? i.Item.ExpectedSizeBytes ?? 0);
        Summary = Items.Count == 0 ? null : $"{Items.Count} élément{(Items.Count > 1 ? "s" : "")} · {ItemRow.FormatSize(total)}";

        var missingTitles = Items.Select(r => r.TitleKey).Distinct()
            .Select(k => _titles.GetValueOrDefault(k))
            .Where(t => t is not null && t.VdmTitleId is null)
            .Cast<TitleInfo>()
            .ToList();
        TitlesToCreate = missingTitles.Count;
        TitlesIncomplete = missingTitles.Count(t => TitleCreator.Check(t).Count > 0);
        RowsToFix = Items.Count(r => r.Status == ItemStatus.FixNeeded && r.IssueCount > 0);
        FilesMissing = Items.Count(r => r.Location is FileLocation.NotFound or FileLocation.Unknown && r.Item.MediaspotAssetId is null);
        FilesToSend = Items.Count(r => r.Location == FileLocation.Source);
        ReadyToImport = Items.Count(r => r.Status is ItemStatus.Ready or ItemStatus.Exported && r.Location == FileLocation.Ingest && r.Item.MediaspotAssetId is null);
        InProgress = Items.Count(r => r.Status == ItemStatus.Pending || r.Item.MediaspotAssetId is not null && r.Status != ItemStatus.Done);
        Done = Items.Count(r => r.Status == ItemStatus.Done);

        OnPropertyChanged(nameof(HasBatch));
        OnPropertyChanged(nameof(TitlesStepText));
        OnPropertyChanged(nameof(FilesStepText));
        OnPropertyChanged(nameof(ImportStepText));
        OnPropertyChanged(nameof(TrackingStepText));
        ItemsView.Refresh();
    }

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Progress = 0;
        Status = message;
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
            UpdateSummary();
        }
    }
}
