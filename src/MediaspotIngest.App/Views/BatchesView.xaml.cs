using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MediaspotIngest.App.ViewModels;

namespace MediaspotIngest.App.Views;

public partial class BatchesView : UserControl
{
    public BatchesView() => InitializeComponent();

    // The "⋯" button opens its menu on a left click too.
    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreButton;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    // DataGrid.SelectedItems is not bindable: hand the multi-selection to the view model.
    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is BatchesViewModel vm)
            vm.SelectedItems = ItemsGrid.SelectedItems.OfType<ItemRow>().ToList();
    }
}

/// <summary>Negates a bool, or turns it into Collapsed/Visible.</summary>
public sealed class Inverse : IValueConverter
{
    public static readonly Inverse Instance = new(asVisibility: false);
    public static readonly Inverse VisibilityInstance = new(asVisibility: true);

    private readonly bool _asVisibility;

    private Inverse(bool asVisibility) => _asVisibility = asVisibility;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var inverted = !(value is true);
        return _asVisibility ? (inverted ? Visibility.Visible : Visibility.Collapsed) : inverted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
