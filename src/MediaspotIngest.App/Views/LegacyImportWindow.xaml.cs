using System.Windows;

namespace MediaspotIngest.App.Views;

public partial class LegacyImportWindow : Window
{
    public LegacyImportWindow() => InitializeComponent();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
