using System.Windows.Controls;
using MediaspotIngest.App.ViewModels;

namespace MediaspotIngest.App.Views;

public partial class TitlesView : UserControl
{
    public TitlesView() => InitializeComponent();

    // DataGrid.SelectedItems is not bindable: hand the multi-selection to the view model.
    private void TitlesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TitlesViewModel vm)
            vm.SelectedTitles = TitlesGrid.SelectedItems.OfType<TitleRow>().ToList();
    }
}
