using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaspotIngest.App.Services;
using MediaspotIngest.Core.Registry;

namespace MediaspotIngest.App.ViewModels;

/// <summary>Picks the registry elements that make a new batch, title by title.</summary>
public sealed partial class ImportRegistryViewModel(IReadOnlySet<string> alreadyInBatches) : ObservableObject
{
    private IReadOnlyList<RegistryElement> _elements = [];

    public ObservableCollection<RegistryTitleChoice> Titles { get; } = [];

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _onlyToIngest = true;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _summary;

    partial void OnOnlyToIngestChanged(bool value) => BuildTitles();

    [RelayCommand]
    private void Browse()
    {
        if (Dialogs.PickFile("Export du registre (*.csv)|*.csv|Tous les fichiers|*.*", FilePath) is not { } path)
            return;

        try
        {
            _elements = RegistryCsvReader.Read(path);
            FilePath = path;
            Description ??= Path.GetFileNameWithoutExtension(path);
            BuildTitles();
        }
        catch (Exception e)
        {
            Dialogs.Error($"Lecture du registre impossible.\n\n{e.Message}");
        }
    }

    [RelayCommand]
    private void SelectAll(string selected)
    {
        foreach (var title in Titles)
            title.IsSelected = bool.Parse(selected);
        UpdateSummary();
    }

    public IReadOnlyList<RegistryElement> SelectedElements() =>
        Titles.Where(t => t.IsSelected).SelectMany(t => t.Elements).ToList();

    private void BuildTitles()
    {
        Titles.Clear();
        var candidates = _elements
            .Where(e => !OnlyToIngest || e.IsToIngest)
            .Where(e => !alreadyInBatches.Contains(e.Id));

        foreach (var group in candidates.GroupBy(e => e.TitleKey).OrderBy(g => g.Key))
        {
            var choice = new RegistryTitleChoice(group.Key, group.First().TitleName, group.ToList());
            choice.PropertyChanged += (_, _) => UpdateSummary();
            Titles.Add(choice);
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var already = _elements.Count(e => alreadyInBatches.Contains(e.Id));
        var selected = SelectedElements();
        Summary = $"{_elements.Count} éléments dans l'export, {already} déjà dans un lot. "
            + $"Sélection : {selected.Count} élément(s), {selected.Sum(e => e.SizeGb):0.#} Go.";
    }
}

public sealed partial class RegistryTitleChoice(string titleKey, string titleName, IReadOnlyList<RegistryElement> elements) : ObservableObject
{
    public string TitleKey { get; } = titleKey;

    public string TitleName { get; } = titleName;

    public IReadOnlyList<RegistryElement> Elements { get; } = elements;

    public int Count => Elements.Count;

    public string Size => $"{Elements.Sum(e => e.SizeGb):0.#} Go";

    [ObservableProperty]
    private bool _isSelected;
}
