using System.Windows;

namespace MediaspotIngest.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();

    public string Status
    {
        set => StatusText.Text = value;
    }
}
