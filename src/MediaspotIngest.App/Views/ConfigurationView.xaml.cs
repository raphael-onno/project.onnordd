using System.Windows;
using System.Windows.Controls;
using MediaspotIngest.App.ViewModels;

namespace MediaspotIngest.App.Views;

/// <summary>PasswordBox cannot be bound: secrets go to the view model (which encrypts them) from here.</summary>
public partial class ConfigurationView : UserControl
{
    public ConfigurationView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ConfigurationViewModel vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    // Never show a previous profile's typed password under another profile.
                    if (args.PropertyName == nameof(ConfigurationViewModel.SelectedProfile))
                    {
                        ProfilePassword.Clear();
                        AsperaPassword.Clear();
                        S3Secret.Clear();
                    }
                    if (args.PropertyName == nameof(ConfigurationViewModel.HasMongoConnectionString))
                        MongoConnectionString.Clear();
                };
            }
        };
    }

    private void ProfilePassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigurationViewModel { SelectedProfile: { } profile })
            profile.SetPassword(ProfilePassword.Password);
    }

    private void AsperaPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigurationViewModel { SelectedProfile: { } profile })
            profile.SetAsperaPassword(AsperaPassword.Password);
    }

    private void S3Secret_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigurationViewModel { SelectedProfile: { } profile })
            profile.SetS3Secret(S3Secret.Password);
    }

    private void MongoConnectionString_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigurationViewModel vm)
            vm.NewMongoConnectionString = MongoConnectionString.Password;
    }
}
