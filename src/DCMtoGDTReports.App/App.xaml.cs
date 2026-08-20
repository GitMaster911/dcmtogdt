using System.Windows;
using System.Windows.Threading;

namespace DCMtoGDTReports.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Unerwartete Fehler duerfen die Anwendung nicht kommentarlos beenden.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unerwarteter Fehler:{Environment.NewLine}{e.Exception.Message}",
            "DCMtoGDTReports",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
