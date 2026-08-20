using System.Windows;
using DCMtoGDTReports.App.ViewModels;
using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Core.Templates;

namespace DCMtoGDTReports.App;

public partial class TemplateEditorWindow : Window
{
    private readonly TemplateEditorViewModel _viewModel;

    public TemplateEditorWindow(AppSettings settings, MeasurementCatalog catalog, SrReport? previewReport = null)
    {
        InitializeComponent();
        _viewModel = new TemplateEditorViewModel(settings, catalog, previewReport);
        DataContext = _viewModel;
    }

    /// <summary>Die bearbeitete Vorlage - nur gueltig, wenn der Dialog mit true geschlossen wurde.</summary>
    public GdtTemplate EditedTemplate => _viewModel.ToTemplate();

    /// <summary>Der Katalog mit der getroffenen Messwertauswahl.</summary>
    public MeasurementCatalog EditedCatalog => _viewModel.ToCatalog();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnPlaceholderDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.InsertPlaceholderCommand.CanExecute(null))
            _viewModel.InsertPlaceholderCommand.Execute(null);
    }
}
