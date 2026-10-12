using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaspotIngest.App.Services;

namespace MediaspotIngest.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppState _state;
    private bool _syncing;

    public MainViewModel(AppState state)
    {
        _state = state;
        Batches = new BatchesViewModel(state) { OpenTitles = () => Navigate(1) };
        Titles = new TitlesViewModel(state);
        Titles.TitlesChanged += (_, _) => _ = Batches.RefreshAsync();
        Configuration = new ConfigurationViewModel(state);

        state.ProfileChanged += (_, _) => OnProfileChanged();
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.MediaspotUser))
                OnPropertyChanged(nameof(ConnectionText));
        };

        RefreshProfileNames();
        // First launch: nothing to work on until a profile exists.
        SelectedTab = state.Profile is null ? 2 : 0;
        _ = Batches.ReloadAsync();
        _ = Titles.ReloadAsync();
    }

    public BatchesViewModel Batches { get; }

    public TitlesViewModel Titles { get; }

    public ConfigurationViewModel Configuration { get; }

    public ObservableCollection<string> ProfileNames { get; } = [];

    [ObservableProperty]
    private string? _selectedProfileName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBatches), nameof(ShowTitles), nameof(ShowConfiguration))]
    private int _selectedTab;

    // Sidebar navigation (radio buttons) bound to the selected page.
    public bool ShowBatches { get => SelectedTab == 0; set { if (value) SelectedTab = 0; } }

    public bool ShowTitles { get => SelectedTab == 1; set { if (value) SelectedTab = 1; } }

    public bool ShowConfiguration { get => SelectedTab == 2; set { if (value) SelectedTab = 2; } }

    public string Version => $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";

    /// <summary>Lets a page send the user to another one (e.g. the batch steps to the Titles page).</summary>
    public void Navigate(int page) => SelectedTab = page;

    public string ConnectionText => _state.MediaspotUser is { } user ? $"Connecté : {user}" : "Mediaspot : non connecté";

    partial void OnSelectedProfileNameChanged(string? value)
    {
        if (!_syncing)
            _state.SelectProfile(value);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            await _state.MediaspotAsync();
        }
        catch (Exception e)
        {
            Dialogs.Error($"Connexion à Mediaspot impossible.\n\n{e.Message}");
        }
    }

    private void OnProfileChanged()
    {
        RefreshProfileNames();
        OnPropertyChanged(nameof(ConnectionText));
        _ = Batches.ReloadAsync();
        _ = Titles.ReloadAsync();
    }

    private void RefreshProfileNames()
    {
        _syncing = true;
        ProfileNames.Clear();
        foreach (var profile in _state.Settings.Profiles)
            ProfileNames.Add(profile.Name);
        SelectedProfileName = _state.Profile?.Name;
        _syncing = false;
    }
}
