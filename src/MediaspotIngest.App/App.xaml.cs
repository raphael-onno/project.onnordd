using System.Windows;
using System.Windows.Threading;
using MediaspotIngest.App.Services;
using MediaspotIngest.App.ViewModels;
using MediaspotIngest.App.Views;
using MediaspotIngest.Core.Configuration;

namespace MediaspotIngest.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var splash = new SplashWindow();
        splash.Show();

        // Keep the splash up long enough to be read, while settings load.
        var minimumDisplay = Task.Delay(TimeSpan.FromMilliseconds(1400));
        splash.Status = "Lecture de la configuration…";
        var state = await Task.Run(() =>
        {
            SettingsStore.MigrateFromPreviousName();
            return new AppState(new SettingsStore(SettingsStore.DefaultFilePath));
        });
        await minimumDisplay;

        var window = new MainWindow { DataContext = new MainViewModel(state) };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        splash.Close();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialogs.Error(e.Exception.Message);
        e.Handled = true;
    }
}
