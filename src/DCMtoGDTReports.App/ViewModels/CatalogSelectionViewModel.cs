using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Mapping;

namespace DCMtoGDTReports.App.ViewModels;

/// <summary>Ein Eintrag der Ankreuzliste im GDT-Editor.</summary>
public sealed class CatalogEntryViewModel(CatalogEntry entry, string suggestedName) : INotifyPropertyChanged
{
    private bool _selected = entry.Selected;
    private string _customName = entry.CustomName;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Eigene deutsche Bezeichnung. Leer = Vorschlag bzw. Originaltext.</summary>
    public string CustomName
    {
        get => _customName;
        set
        {
            _customName = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveName));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Was tatsaechlich im Befund erscheint.</summary>
    public string EffectiveName => string.IsNullOrWhiteSpace(CustomName) ? suggestedName : CustomName;

    /// <summary>Originaltext aus dem DICOM-Bericht.</summary>
    public string OriginalName => entry.DisplayName;

    public string KindText => entry.KindText;

    public string Label => entry.Label;

    public string Unit => entry.Unit;

    public int SeenCount => entry.SeenCount;

    public string Info => entry.SeenCount > 0
        ? $"{entry.SeenCount}x gesehen{(string.IsNullOrWhiteSpace(entry.Unit) ? string.Empty : $" - {entry.Unit}")}"
        : string.Empty;

    public CatalogEntry Model => entry;

    public event EventHandler? Changed;

    public void Apply()
    {
        entry.Selected = Selected;
        entry.CustomName = CustomName.Trim();
    }

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
    private readonly MeasurementMapper _mapper;

    public CatalogSelectionViewModel(MeasurementCatalog catalog, MeasurementMapper mapper)
    {
        _catalog = catalog;
        _mapper = mapper;
        _useSelection = catalog.Enabled;
        _includeUnknown = catalog.IncludeUnknown;

        Fill(Measurements, catalog.Measurements);
        Fill(Regions, catalog.Regions);
        Fill(ImageModes, catalog.ImageModes);

        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        ResetNamesCommand = new RelayCommand(ResetNames);
    }

    public ObservableCollection<CatalogEntryViewModel> Measurements { get; } = [];
    public ObservableCollection<CatalogEntryViewModel> Regions { get; } = [];
    public ObservableCollection<CatalogEntryViewModel> ImageModes { get; } = [];

    /// <summary>Alle Eintraege zusammen - so laesst sich alles in einer Tabelle bearbeiten.</summary>
    public ObservableCollection<CatalogEntryViewModel> AllEntries { get; } = [];

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand ResetNamesCommand { get; }

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

    /// <summary>Uebertraegt die Ankreuzungen und Bezeichnungen zurueck in den Katalog.</summary>
    public MeasurementCatalog ToCatalog()
    {
        _catalog.Enabled = UseSelection;
        _catalog.IncludeUnknown = IncludeUnknown;

        foreach (var entry in AllEntries) entry.Apply();

        return _catalog;
    }

    private void Fill(ObservableCollection<CatalogEntryViewModel> target, List<CatalogEntry> source)
    {
        foreach (var entry in source)
        {
            var vm = new CatalogEntryViewModel(entry, Suggest(entry));
            vm.Changed += (_, _) => Raise();
            target.Add(vm);
            AllEntries.Add(vm);
        }
    }

    /// <summary>Die eingebaute deutsche Bezeichnung, falls es eine gibt.</summary>
    private string Suggest(CatalogEntry entry) => entry.Kind switch
    {
        CatalogEntryKind.Region => _mapper.ResolveRegion(entry.DisplayName),
        CatalogEntryKind.ImageMode => _mapper.ResolveImageMode(entry.DisplayName),
        _ => string.IsNullOrWhiteSpace(entry.ShortName) ? entry.DisplayName : entry.ShortName
    };

    private void SetAll(bool selected)
    {
        foreach (var entry in AllEntries) entry.Selected = selected;
    }

    /// <summary>Setzt alle eigenen Bezeichnungen zurueck auf die Vorgaben.</summary>
    private void ResetNames()
    {
        foreach (var entry in AllEntries) entry.CustomName = string.Empty;
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
