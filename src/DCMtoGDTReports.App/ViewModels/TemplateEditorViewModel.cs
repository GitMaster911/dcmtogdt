using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Filtering;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Mapping;
using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Core.Templates;

namespace DCMtoGDTReports.App.ViewModels;

/// <summary>
/// Eine Zeile im Vorlagen-Editor. Aenderungen loesen sofort eine neue Vorschau aus.
/// </summary>
public sealed class TemplateLineViewModel : INotifyPropertyChanged
{
    public TemplateLineViewModel(GdtTemplateLine line)
    {
        _enabled = line.Enabled;
        _fieldId = line.FieldId;
        _content = line.Content;
        _description = line.Description;
    }

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    private string _fieldId;
    public string FieldId
    {
        get => _fieldId;
        set { _fieldId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFieldIdValid)); }
    }

    private string _content;
    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    private string _description;
    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public bool IsFieldIdValid => GdtTemplateEngine.IsValidFieldId(FieldId);

    public GdtTemplateLine ToModel() => new()
    {
        Enabled = Enabled,
        FieldId = FieldId?.Trim() ?? string.Empty,
        Content = Content ?? string.Empty,
        Description = Description ?? string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// ViewModel des GDT-Vorlagen-Editors: Zeilen an- und abwaehlen, Platzhalter einsetzen,
/// Ergebnis sofort als Vorschau sehen.
/// </summary>
public sealed class TemplateEditorViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private readonly GdtSettings _gdtSettings;
    private readonly SrReport _previewReport;
    private readonly Encoding _encoding;

    public TemplateEditorViewModel(AppSettings settings, MeasurementCatalog catalog, SrReport? previewReport = null)
    {
        _settings = settings;
        _gdtSettings = settings.Gdt;
        _previewReport = previewReport ?? CreateDemoReport();
        _encoding = new GdtFileWriter(settings.Gdt).Encoding;

        Catalog = new CatalogSelectionViewModel(catalog);
        Catalog.SelectionChanged += (_, _) => UpdatePreview();

        var template = settings.GdtTemplate is { Lines.Count: > 0 }
            ? settings.GdtTemplate
            : GdtTemplate.CreateDefault();

        _useTemplate = template.Enabled;
        foreach (var line in template.Lines) Add(new TemplateLineViewModel(line));

        AddLineCommand = new RelayCommand(AddLine);
        RemoveLineCommand = new RelayCommand(RemoveLine, () => SelectedLine is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => SelectedLine is not null);
        MoveDownCommand = new RelayCommand(() => Move(1), () => SelectedLine is not null);
        RestoreDefaultCommand = new RelayCommand(RestoreDefault);
        InsertPlaceholderCommand = new RelayCommand(InsertPlaceholder, () => SelectedLine is not null && SelectedPlaceholder is not null);

        UpdatePreview();
    }

    public CatalogSelectionViewModel Catalog { get; }

    public ObservableCollection<TemplateLineViewModel> Lines { get; } = [];

    public IReadOnlyList<GdtPlaceholder> Placeholders { get; } = GdtTemplateEngine.Placeholders;

    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand RestoreDefaultCommand { get; }
    public RelayCommand InsertPlaceholderCommand { get; }

    private bool _useTemplate;
    public bool UseTemplate
    {
        get => _useTemplate;
        set { _useTemplate = value; OnPropertyChanged(); UpdatePreview(); }
    }

    private TemplateLineViewModel? _selectedLine;
    public TemplateLineViewModel? SelectedLine
    {
        get => _selectedLine;
        set
        {
            _selectedLine = value;
            OnPropertyChanged();
            RemoveLineCommand.RaiseCanExecuteChanged();
            MoveUpCommand.RaiseCanExecuteChanged();
            MoveDownCommand.RaiseCanExecuteChanged();
            InsertPlaceholderCommand.RaiseCanExecuteChanged();
        }
    }

    private GdtPlaceholder? _selectedPlaceholder;
    public GdtPlaceholder? SelectedPlaceholder
    {
        get => _selectedPlaceholder;
        set { _selectedPlaceholder = value; OnPropertyChanged(); InsertPlaceholderCommand.RaiseCanExecuteChanged(); }
    }

    private string _preview = string.Empty;
    public string Preview
    {
        get => _preview;
        private set { _preview = value; OnPropertyChanged(); }
    }

    private string _measurementCount = string.Empty;
    public string MeasurementCount
    {
        get => _measurementCount;
        private set { _measurementCount = value; OnPropertyChanged(); }
    }

    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        private set { _validationMessage = value; OnPropertyChanged(); }
    }

    public GdtTemplate ToTemplate() => new()
    {
        Enabled = UseTemplate,
        Lines = Lines.Select(l => l.ToModel()).ToList()
    };

    /// <summary>Der Katalog mit den in der Oberflaeche gesetzten Haken.</summary>
    public MeasurementCatalog ToCatalog() => Catalog.ToCatalog();

    private void Add(TemplateLineViewModel line)
    {
        line.PropertyChanged += (_, _) => UpdatePreview();
        Lines.Add(line);
    }

    private void AddLine()
    {
        var line = new TemplateLineViewModel(new GdtTemplateLine
        {
            FieldId = "6220",
            Content = string.Empty,
            Description = "Neue Zeile"
        });

        line.PropertyChanged += (_, _) => UpdatePreview();

        var index = SelectedLine is null ? Lines.Count : Lines.IndexOf(SelectedLine) + 1;
        Lines.Insert(index, line);
        SelectedLine = line;
        UpdatePreview();
    }

    private void RemoveLine()
    {
        if (SelectedLine is null) return;

        var index = Lines.IndexOf(SelectedLine);
        Lines.Remove(SelectedLine);
        SelectedLine = Lines.Count == 0 ? null : Lines[Math.Min(index, Lines.Count - 1)];
        UpdatePreview();
    }

    private void Move(int offset)
    {
        if (SelectedLine is null) return;

        var index = Lines.IndexOf(SelectedLine);
        var target = index + offset;
        if (target < 0 || target >= Lines.Count) return;

        Lines.Move(index, target);
        UpdatePreview();
    }

    private void RestoreDefault()
    {
        Lines.Clear();
        foreach (var line in GdtTemplate.CreateDefault().Lines) Add(new TemplateLineViewModel(line));
        SelectedLine = null;
        UpdatePreview();
    }

    private void InsertPlaceholder()
    {
        if (SelectedLine is null || SelectedPlaceholder is null) return;

        var separator = string.IsNullOrEmpty(SelectedLine.Content) || SelectedLine.Content.EndsWith(' ')
            ? string.Empty
            : " ";
        SelectedLine.Content += separator + SelectedPlaceholder.Token;
    }

    private void UpdatePreview()
    {
        var invalid = Lines.Where(l => l.Enabled && !l.IsFieldIdValid).ToList();
        ValidationMessage = invalid.Count == 0
            ? string.Empty
            : $"{invalid.Count} aktive Zeile(n) haben keine gueltige vierstellige Feldkennung und werden uebersprungen.";

        try
        {
            var report = BuildFilteredPreviewReport();
            var builder = new Gdt6310Builder(_gdtSettings) { Template = ToTemplate() };
            var fields = builder.BuildFields(report);

            Preview = string.Join(Environment.NewLine,
                fields.Where(f => f.FieldId is "6220" or "6227").Select(f => f.Content));
            MeasurementCount = $"{report.Measurements.Count} von {report.AllMeasurements.Count} Messwerten";
        }
        catch (Exception ex)
        {
            Preview = $"Vorschau nicht moeglich: {ex.Message}";
            MeasurementCount = string.Empty;
        }
    }

    /// <summary>Wendet die aktuelle Ankreuzauswahl auf die Vorschaudaten an.</summary>
    private SrReport BuildFilteredPreviewReport()
    {
        var source = _previewReport.AllMeasurements.Count > 0
            ? _previewReport.AllMeasurements
            : _previewReport.Measurements;

        var report = new SrReport
        {
            Header = _previewReport.Header,
            Engine = _previewReport.Engine,
            Measurements = source,
            RawMeasurementCount = _previewReport.RawMeasurementCount
        };

        var filter = new MeasurementFilter(
            _settings.MeasurementFilter,
            _gdtSettings,
            new MeasurementMapper(_settings.MeasurementShortNames, _settings.MethodShortNames),
            Catalog.ToCatalog());

        report.ApplyFilteredMeasurements(filter.Apply(source));
        return report;
    }

    /// <summary>Beispieldaten, falls noch keine SR-Datei analysiert wurde.</summary>
    private static SrReport CreateDemoReport()
    {
        MeasurementResult Value(string shortName, string value, string unit, string note = "") => new()
        {
            Name = shortName,
            ShortName = shortName,
            Value = value,
            RawValue = value,
            Unit = unit,
            Group = "Left Ventricle / 2D mode",
            AggregationNote = note
        };

        return new SrReport
        {
            Engine = "Vorschau",
            RawMeasurementCount = 4,
            Header = new SrHeader
            {
                PatientId = "12345",
                LastName = "Muster",
                FirstName = "Erika",
                PatientBirthDate = "19850312",
                PatientSex = "F",
                StudyDate = "20240115",
                StudyTime = "103015",
                AccessionNumber = "1000042",
                SopInstanceUid = "1.2.826.0.1.3680043.9.9999.1.3",
                StudyInstanceUid = "1.2.826.0.1.3680043.9.9999.1.1",
                Manufacturer = "GE Vingmed Ultrasound",
                ManufacturerModelName = "Vivid T8",
                StationName = "VIVIDT8-DEMO",
                DocumentTitle = "Adult Echocardiography Procedure Report"
            },
            Measurements =
            [
                Value("LVIDd", "4.21", "cm"),
                Value("IVSd", "0.64", "cm"),
                Value("EF", "58.53", "%", "Min 49.82 / Max 67.28, n=6")
            ]
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
