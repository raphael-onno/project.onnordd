using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaspotIngest.App.Services;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Filling;
using MediaspotIngest.Core.Mediaspot;

namespace MediaspotIngest.App.ViewModels;

public sealed partial class TitlesViewModel : ObservableObject
{
    private readonly AppState _state;

    public TitlesViewModel(AppState state)
    {
        _state = state;
        View = CollectionViewSource.GetDefaultView(Titles);
        View.Filter = o => o is TitleRow row && Matches(row);
    }

    public ObservableCollection<TitleRow> Titles { get; } = [];

    /// <summary>Raised after titles were saved, so the batch page refreshes its rows and steps.</summary>
    public event EventHandler? TitlesChanged;

    public ICollectionView View { get; }

    public IReadOnlyList<string> TitleTypes { get; } = ["Movie", "Season"];

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private bool _onlyIncomplete;

    [ObservableProperty]
    private string? _status;

    partial void OnSearchChanged(string value) => View.Refresh();

    partial void OnOnlyIncompleteChanged(bool value) => View.Refresh();

    public async Task ReloadAsync()
    {
        Titles.Clear();
        if (_state.Profile is not { } profile)
            return;

        try
        {
            foreach (var title in await _state.Store.GetTitlesAsync(profile.Name))
                Titles.Add(new TitleRow(title));
            Status = $"{Titles.Count} titres.";
        }
        catch (Exception e)
        {
            Status = $"Chargement impossible : {e.Message}";
        }
    }

    [RelayCommand]
    private Task Reload() => ReloadAsync();

    /// <summary>Saves the edited titles and copies their values into every item of those titles.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var changed = Titles.Where(t => t.IsDirty).ToList();
        if (changed.Count == 0)
        {
            Status = "Aucune modification.";
            return;
        }

        var profile = _state.RequireProfile();
        var template = _state.RequireTemplate();
        await _state.Store.SaveTitlesAsync(changed.Select(t => t.ToTitle()));

        var keys = changed.ToDictionary(t => t.TitleKey, t => t.ToTitle());
        var items = (await _state.Store.GetItemsAsync(profile.Name)).Where(i => keys.ContainsKey(i.TitleKey)).ToList();
        var builder = new ItemRowBuilder(template, profile);
        foreach (var item in items)
            builder.FillTitle(item, keys[item.TitleKey]);
        await _state.Store.SaveItemsAsync(items);

        foreach (var row in changed)
            row.IsDirty = false;
        Status = $"{changed.Count} titre(s) enregistré(s), {items.Count} élément(s) mis à jour.";
        TitlesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rows selected in the grid, set from the view.</summary>
    public IReadOnlyList<TitleRow> SelectedTitles { get; set; } = [];

    /// <summary>
    /// Creates the selected titles in Mediaspot (or finds them by Title ID), as the old tool's Custom Titles Manager did,
    /// then writes their Mediaspot id into the items.
    /// </summary>
    [RelayCommand]
    private async Task CreateInMediaspotAsync()
    {
        var rows = SelectedTitles.Count > 0 ? SelectedTitles.ToList() : View.Cast<TitleRow>().Where(t => t.VdmTitleId is null).ToList();
        if (rows.Count == 0)
        {
            Status = "Aucun titre à créer.";
            return;
        }

        if (!Dialogs.Confirm($"Créer {rows.Count} titre(s) dans Mediaspot ?\nUn titre qui existe déjà avec le même Title ID est réutilisé, jamais dupliqué."))
            return;

        var profile = _state.RequireProfile();
        var creator = new TitleCreator(await _state.MediaspotAsync());
        int created = 0, found = 0, failed = 0;
        foreach (var row in rows)
        {
            Status = $"Création de « {row.MainTitle} »…";
            try
            {
                var result = await creator.CreateOrFindAsync(row.ToTitle(), profile.DefaultOriginalLanguage, profile.DefaultPictureFormat);
                row.MediaspotResult = result.Outcome switch
                {
                    TitleCreationOutcome.Created => $"créé ({result.TitleId})",
                    TitleCreationOutcome.Found => $"existant ({result.TitleId})",
                    _ => result.Error,
                };
                if (result.TitleId is { } id)
                {
                    row.VdmTitleId = id;
                    if (result.Outcome == TitleCreationOutcome.Created) created++; else found++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception e)
            {
                row.MediaspotResult = e.Message;
                failed++;
            }
        }

        await SaveAsync();
        Status = $"{created} titre(s) créé(s), {found} déjà existant(s), {failed} en échec (voir la colonne Mediaspot).";
    }

    private bool Matches(TitleRow row)
    {
        if (OnlyIncomplete && row.IsComplete)
            return false;
        return string.IsNullOrWhiteSpace(Search)
            || row.TitleKey.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || row.MainTitle.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || (row.TitleCustomId ?? "").Contains(Search, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class TitleRow : ObservableObject
{
    private readonly TitleInfo _title;

    public TitleRow(TitleInfo title)
    {
        _title = title;
        _titleCustomId = title.TitleCustomId;
        _mainTitle = title.MainTitle;
        _titleType = title.TitleType;
        _originalLanguage = title.OriginalLanguage;
        _pictureFormat = title.PictureFormat;
        _productionYear = title.ProductionYear;
        _seasonNumber = title.SeasonNumber;
        _episodesCount = title.EpisodesCount;
        _vdmTitleId = title.VdmTitleId;
    }

    /// <summary>The title's id in Mediaspot, once created or found there.</summary>
    [ObservableProperty]
    private long? _vdmTitleId;

    /// <summary>Outcome of the last "Créer dans Mediaspot", for this row.</summary>
    [ObservableProperty]
    private string? _mediaspotResult;

    public string TitleKey => _title.TitleKey;

    [ObservableProperty] private string? _titleCustomId;
    [ObservableProperty] private string _mainTitle;
    [ObservableProperty] private string _titleType;
    [ObservableProperty] private string? _originalLanguage;
    [ObservableProperty] private string? _pictureFormat;
    [ObservableProperty] private int? _productionYear;
    [ObservableProperty] private string? _seasonNumber;
    [ObservableProperty] private int? _episodesCount;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>What Mediaspot needs to create the title (the Title ID is optional).</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(MainTitle) && ProductionYear is not null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(IsDirty) or nameof(IsComplete) or nameof(MediaspotResult)))
        {
            IsDirty = true;
            OnPropertyChanged(nameof(IsComplete));
        }
    }

    public TitleInfo ToTitle() => new()
    {
        ProfileName = _title.ProfileName,
        TitleKey = _title.TitleKey,
        TitleCustomId = string.IsNullOrWhiteSpace(TitleCustomId) ? null : TitleCustomId.Trim(),
        VdmTitleId = VdmTitleId,
        MainTitle = MainTitle.Trim(),
        TitleType = TitleType,
        OriginalLanguage = string.IsNullOrWhiteSpace(OriginalLanguage) ? null : OriginalLanguage.Trim().ToUpperInvariant(),
        PictureFormat = string.IsNullOrWhiteSpace(PictureFormat) ? null : PictureFormat.Trim().Replace(',', '.'),
        ProductionYear = ProductionYear,
        SeasonNumber = string.IsNullOrWhiteSpace(SeasonNumber) ? null : SeasonNumber.Trim(),
        EpisodesCount = EpisodesCount,
    };
}
