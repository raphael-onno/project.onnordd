using System.Windows;
using MediaspotIngest.App.ViewModels;
using MediaspotIngest.Core.Configuration;
using MediaspotIngest.Core.Files;

namespace MediaspotIngest.App.Views;

public partial class TransferPlanWindow : Window
{
    public TransferPlanWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not TransferPlan plan)
                return;
            var runnable = plan.Runnable.Count();
            var verb = plan.Mode == FileTransferMode.Move ? "déplacé(s)" : "copié(s)";
            var removal = plan.Method is "Aspera" or "S3" && plan.Mode == FileTransferMode.Move
                ? " La source est supprimée seulement une fois le transfert réussi."
                : "";
            SummaryText.Text = $"{plan.Method} : {runnable} élément(s) {verb} ({ItemRow.FormatSize(plan.TotalBytes)}), "
                + $"{plan.Steps.Count - runnable} ignoré(s).{removal}";
        };
    }

    private void Run_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
