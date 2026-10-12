using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace MediaspotIngest.App.Services;

public static class Dialogs
{
    private const string Caption = "onnoRDD";

    public static void Error(string message) =>
        MessageBox.Show(Owner(), message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Info(string message) =>
        MessageBox.Show(Owner(), message, Caption, MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(string message) =>
        MessageBox.Show(Owner(), message, Caption, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public static string? PickFile(string filter, string? initialPath = null)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(Path.GetDirectoryName(initialPath)))
            dialog.InitialDirectory = Path.GetDirectoryName(initialPath);
        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    public static string? PickFolder(string? initialPath = null)
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            dialog.InitialDirectory = initialPath;
        return dialog.ShowDialog(Owner()) == true ? dialog.FolderName : null;
    }

    public static void Open(string pathOrUrl) =>
        Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });

    public static void ShowInExplorer(string filePath) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });

    public static Window? Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
}
