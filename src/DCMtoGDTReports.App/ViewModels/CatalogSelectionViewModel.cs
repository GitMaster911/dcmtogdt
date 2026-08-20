using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DCMtoGDTReports.Core.Catalog;

namespace DCMtoGDTReports.App.ViewModels;

/// <summary>Ein Eintrag der Ankreuzliste im GDT-Editor.</summary>
public sealed class CatalogEntryViewModel(CatalogEntry entry) : INotifyPropertyChanged
{
    private bool _selected = entry.Selected;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    }

    public string Label => entry.Label;

    public string Unit => entry.Unit;

    public int SeenCount => entry.SeenCount;

    public string Info => entry.SeenCount > 0
        ? $"{entry.SeenCount}x gesehen{(string.IsNullOrWhiteSpace(entry.Unit) ? string.Empty : $" - {entry.Unit}")}"
        : string.Empty;

    public CatalogEntry Model => entry;

    public event EventHandler? SelectionChanged;

    public void Apply() => entry.Selected = Selected;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Ankreuzlisten fuer Messgroessen, Regionen und Aufnahmemodi - gefuellt aus dem Katalog,
/// den das Programm beim Auswerten von SR-Dateien lernt.
/// </summary>
public sealed class CatalogSelectionViewModel : INotifyPropertyChanged
{
    private readonly MeasurementCatalog _catalog;

    public CatalogSelectionViewModel(MeasurementCatalog catalog)
    {
        _catalog = catalog;
        _useSelection = catalog.Enabled;
        _includeUnknown = catalog.IncludeUnknown;

        Fill(Measurements, catalog.Measurements);
        Fill(Regions, catalog.Regions);
        Fill(ImageModes, catalog.ImageModes);

        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
    }

    public ObservableCollection<CatalogEntryViewModel> Measurements { get; } = [];
    public ObservableCollection<CatalogEntryViewModel> Regions { get; } = [];
    public ObservableCollection<CatalogEntryViewModel> ImageModes { get; } = [];

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }

    /// <summary>Wird ausgeloest, sobald sich an der Auswahl etwas aendert.</summary>
    public event EventHandler? SelectionChanged;

    private bool _useSelection;
    public bool UseSelection
    {
        get => _useSelection;
        set { _useSelection = value; OnPropertyChanged(); Raise(); }
    }

    private bool _includeUnknown;
    public bool IncludeUnknown
    {
        get => _includeUnknown;
        set { _includeUnknown = value; OnPropertyChanged(); Raise(); }
    }

    public bool IsEmpty => _catalog.IsEmpty;

    public string StatusText => _catalog.IsEmpty
        ? "Noch nichts gelernt. Werten Sie eine SR-Datei aus oder nutzen Sie \"Aus SR-Dateien lernen\"."
        : $"{Measurements.Count} Messgroessen, {Regions.Count} Regionen, {ImageModes.Count} Modi "
          + $"aus {_catalog.LearnedFileCount} Auswertung(en).";

    public string SelectionSummary =>
        $"{Measurements.Count(m => m.Selected)}/{Measurements.Count} Messgroessen, "
        + $"{Regions.Count(r => r.Selected)}/{Regions.Count} Regionen, "
        + $"{ImageModes.Count(i => i.Selected)}/{ImageModes.Count} Modi ausgewaehlt";

    /// <summary>Uebertraegt die Ankreuzungen zurueck in den Katalog.</summary>
    public MeasurementCatalog ToCatalog()
    {
        _catalog.Enabled = UseSelection;
        _catalog.IncludeUnknown = IncludeUnknown;

        foreach (var entry in Measurements.Concat(Regions).Concat(ImageModes))
            entry.Apply();

        return _catalog;
    }

    private void Fill(ObservableCollection<CatalogEntryViewModel> target, List<CatalogEntry> source)
    {
        foreach (var entry in source)
        {
            var vm = new CatalogEntryViewModel(entry);
            vm.SelectionChanged += (_, _) => Raise();
            target.Add(vm);
        }
    }

    private void SetAll(bool selected)
    {
        foreach (var entry in Measurements.Concat(Regions).Concat(ImageModes))
            entry.Selected = selected;
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(SelectionSummary));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
