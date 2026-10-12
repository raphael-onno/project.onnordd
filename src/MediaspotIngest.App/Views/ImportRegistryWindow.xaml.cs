using System.Windows;
using MediaspotIngest.App.Services;
using MediaspotIngest.App.ViewModels;

namespace MediaspotIngest.App.Views;

public partial class ImportRegistryWindow : Window
{
    public ImportRegistryWindow() => InitializeComponent();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ImportRegistryViewModel vm && vm.SelectedElements().Count == 0)
        {
            Dialogs.Info("Cocher au moins un titre.");
            return;
        }

        DialogResult = true;
    }
}
