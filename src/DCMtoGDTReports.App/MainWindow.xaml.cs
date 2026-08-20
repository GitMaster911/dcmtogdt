using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DCMtoGDTReports.App.ViewModels;

namespace DCMtoGDTReports.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (ViewModel is not null)
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Log-Anzeige automatisch nach unten scrollen.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LogText))
            Dispatcher.BeginInvoke(() => LogBox.ScrollToEnd());
    }

    protected override void OnClosed(EventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Shutdown();
        }

        base.OnClosed(e);
    }
}
