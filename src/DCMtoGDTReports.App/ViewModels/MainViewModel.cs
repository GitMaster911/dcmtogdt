using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Logging;
using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Core.Processing;
using DCMtoGDTReports.Core.Registry;
using DCMtoGDTReports.Core.Updating;
using DCMtoGDTReports.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DCMtoGDTReports.App.ViewModels;

/// <summary>
/// Zeile in der Liste "Zuletzt verarbeitete Dateien".
/// </summary>
public sealed class ProcessedFileRow
{
    public required string Time { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public required string GdtFile { get; init; }
    public required string Info { get; init; }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settingsService;
    private readonly InMemoryLoggerProvider _logProvider = new();
    private readonly ILoggerFactory _loggerFactory;

    private AppSettings _settings;
    private SqliteProcessedFileRegistry _registry;
    private SrFileProcessor _processor;
    private FolderWatcherService? _watcher;
    private UpdateManifest? _pendingUpdate;
    private SrReport? _lastReport;

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        SettingsService.EnsureFolders(_settings);

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(_logProvider);
        });

        _registry = CreateRegistry();
        _processor = CreateProcessor();

        _logProvider.EntryAdded += (_, entry) => Application.Current?.Dispatcher.Invoke(() => AppendLog(entry));

        AnalyzeTestFileCommand = new RelayCommand(AnalyzeTestFileAsync);
        CreateTestGdtCommand = new RelayCommand(CreateTestGdtAsync);
        ToggleWatcherCommand = new RelayCommand(ToggleWatcher);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        SetupDcmtkCommand = new RelayCommand(SetupDcmtkAsync);
        OpenOutputFolderCommand = new RelayCommand(() => OpenFolder(OutputFolder));
        RefreshCommand = new RelayCommand(RefreshDashboard);
        ClearLogCommand = new RelayCommand(() => { LogText = string.Empty; _logProvider.Clear(); });

        BrowseInputCommand = new RelayCommand(() => BrowseFolder(v => InputFolder = v, InputFolder));
        BrowseOutputCommand = new RelayCommand(() => BrowseFolder(v => OutputFolder = v, OutputFolder));
        BrowseArchiveCommand = new RelayCommand(() => BrowseFolder(v => ArchiveFolder = v, ArchiveFolder));
        BrowseErrorCommand = new RelayCommand(() => BrowseFolder(v => ErrorFolder = v, ErrorFolder));
        BrowseDcmtkCommand = new RelayCommand(() => BrowseFolder(v => DcmtkPath = v, DcmtkPath));
        BrowseForwardCommand = new RelayCommand(() => BrowseFolder(v => ForwardFolder = v, ForwardFolder));

        CheckForUpdatesCommand = new RelayCommand(() => CheckForUpdatesAsync(silent: false));
        InstallUpdateCommand = new RelayCommand(InstallUpdateAsync, () => _pendingUpdate is not null);
        EditTemplateCommand = new RelayCommand(EditTemplate);
        ApplyCompactProfileCommand = new RelayCommand(ApplyCompactProfile);
        LearnFromFilesCommand = new RelayCommand(LearnFromFilesAsync);

        RefreshDcmtkStatus();
        RefreshDashboard();
        Log($"Konfiguration: {_settingsService.SettingsFilePath}");

        if (_settings.Update is { Enabled: true, CheckOnStartup: true })
            _ = CheckForUpdatesAsync(silent: true);
    }

    #region Commands

    public RelayCommand AnalyzeTestFileCommand { get; }
    public RelayCommand CreateTestGdtCommand { get; }
    public RelayCommand ToggleWatcherCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand SetupDcmtkCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand BrowseInputCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand BrowseArchiveCommand { get; }
    public RelayCommand BrowseErrorCommand { get; }
    public RelayCommand BrowseDcmtkCommand { get; }
    public RelayCommand BrowseForwardCommand { get; }
    public RelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand InstallUpdateCommand { get; }
    public RelayCommand EditTemplateCommand { get; }
    public RelayCommand ApplyCompactProfileCommand { get; }
    public RelayCommand LearnFromFilesCommand { get; }

    #endregion

    #region Bindbare Eigenschaften

    public ObservableCollection<ProcessedFileRow> RecentFiles { get; } = [];

    public string SettingsFilePath => _settingsService.SettingsFilePath;

    public string InputFolder
    {
        get => _settings.InputFolder;
        set { _settings.InputFolder = value; OnPropertyChanged(); }
    }

    public string OutputFolder
    {
        get => _settings.OutputFolder;
        set { _settings.OutputFolder = value; OnPropertyChanged(); }
    }

    public string ArchiveFolder
    {
        get => _settings.ArchiveFolder;
        set { _settings.ArchiveFolder = value; OnPropertyChanged(); }
    }

    public string ErrorFolder
    {
        get => _settings.ErrorFolder;
        set { _settings.ErrorFolder = value; OnPropertyChanged(); }
    }

    public string DcmtkPath
    {
        get => _settings.DcmtkPath;
        set { _settings.DcmtkPath = value; OnPropertyChanged(); RefreshDcmtkStatus(); }
    }

    public string SenderId
    {
        get => _settings.Gdt.SenderId;
        set { _settings.Gdt.SenderId = value; OnPropertyChanged(); }
    }

    public string ReceiverId
    {
        get => _settings.Gdt.ReceiverId;
        set { _settings.Gdt.ReceiverId = value; OnPropertyChanged(); }
    }

    public string TestType
    {
        get => _settings.Gdt.TestType;
        set { _settings.Gdt.TestType = value; OnPropertyChanged(); }
    }

    public string TestId
    {
        get => _settings.Gdt.TestId;
        set { _settings.Gdt.TestId = value; OnPropertyChanged(); }
    }

    public string FilePattern
    {
        get => _settings.Processing.FilePattern;
        set { _settings.Processing.FilePattern = value; OnPropertyChanged(); }
    }

    public string ForwardFolder
    {
        get => _settings.Processing.ForwardFolder;
        set { _settings.Processing.ForwardFolder = value; OnPropertyChanged(); }
    }

    public bool ForwardAllFiles
    {
        get => _settings.Processing.ForwardAllFiles;
        set { _settings.Processing.ForwardAllFiles = value; OnPropertyChanged(); }
    }

    public bool FilterEnabled
    {
        get => _settings.MeasurementFilter.Enabled;
        set { _settings.MeasurementFilter.Enabled = value; OnPropertyChanged(); }
    }

    public bool FilterOnlyMapped
    {
        get => _settings.MeasurementFilter.OnlyMappedMeasurements;
        set { _settings.MeasurementFilter.OnlyMappedMeasurements = value; OnPropertyChanged(); }
    }

    public bool FilterOnlySelected
    {
        get => _settings.MeasurementFilter.OnlySelectedValues;
        set { _settings.MeasurementFilter.OnlySelectedValues = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<RepeatedValueMode> RepeatedValueModes { get; } = Enum.GetValues<RepeatedValueMode>();

    public RepeatedValueMode FilterRepeatedValues
    {
        get => _settings.MeasurementFilter.RepeatedValues;
        set { _settings.MeasurementFilter.RepeatedValues = value; OnPropertyChanged(); }
    }

    public int FilterMaxMeasurements
    {
        get => _settings.MeasurementFilter.MaxMeasurements;
        set { _settings.MeasurementFilter.MaxMeasurements = value; OnPropertyChanged(); }
    }

    /// <summary>Musterlisten werden in der GUI als kommagetrennte Zeile gepflegt.</summary>
    public string FilterExcludeFindingSites
    {
        get => string.Join(", ", _settings.MeasurementFilter.ExcludeFindingSites);
        set { _settings.MeasurementFilter.ExcludeFindingSites = SplitPatterns(value); OnPropertyChanged(); }
    }

    public string FilterIncludeConcepts
    {
        get => string.Join(", ", _settings.MeasurementFilter.IncludeConcepts);
        set { _settings.MeasurementFilter.IncludeConcepts = SplitPatterns(value); OnPropertyChanged(); }
    }

    public string FilterExcludeConcepts
    {
        get => string.Join(", ", _settings.MeasurementFilter.ExcludeConcepts);
        set { _settings.MeasurementFilter.ExcludeConcepts = SplitPatterns(value); OnPropertyChanged(); }
    }

    private static List<string> SplitPatterns(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private string _statusText = "Bereit";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    private string _dcmtkStatus = string.Empty;
    public string DcmtkStatus
    {
        get => _dcmtkStatus;
        private set { _dcmtkStatus = value; OnPropertyChanged(); }
    }

    private bool _dcmtkAvailable;
    public bool DcmtkAvailable
    {
        get => _dcmtkAvailable;
        private set { _dcmtkAvailable = value; OnPropertyChanged(); }
    }

    public bool IsWatching => _watcher?.IsRunning == true;

    public string WatcherButtonText => IsWatching ? "Ordnerueberwachung stoppen" : "Ordnerueberwachung starten";

    public string WatcherStateText => IsWatching ? "Aktiv" : "Gestoppt";

    private int _successCount;
    public int SuccessCount
    {
        get => _successCount;
        private set { _successCount = value; OnPropertyChanged(); }
    }

    private int _failedCount;
    public int FailedCount
    {
        get => _failedCount;
        private set { _failedCount = value; OnPropertyChanged(); }
    }

    private int _skippedCount;
    public int SkippedCount
    {
        get => _skippedCount;
        private set { _skippedCount = value; OnPropertyChanged(); }
    }

    private int _pendingCount;
    public int PendingCount
    {
        get => _pendingCount;
        private set { _pendingCount = value; OnPropertyChanged(); }
    }

    private string _logText = string.Empty;
    public string LogText
    {
        get => _logText;
        private set { _logText = value; OnPropertyChanged(); }
    }

    private string _analysisText = "Noch keine Analyse durchgefuehrt.";
    public string AnalysisText
    {
        get => _analysisText;
        private set { _analysisText = value; OnPropertyChanged(); }
    }

    public string InstalledVersion => UpdateService.InstalledVersion.ToString(3);

    public bool UpdateEnabled
    {
        get => _settings.Update.Enabled;
        set { _settings.Update.Enabled = value; OnPropertyChanged(); }
    }

    public string UpdateManifestUrl
    {
        get => _settings.Update.ManifestUrl;
        set { _settings.Update.ManifestUrl = value; OnPropertyChanged(); }
    }

    public string UpdateServiceName
    {
        get => _settings.Update.ServiceName;
        set { _settings.Update.ServiceName = value; OnPropertyChanged(); }
    }

    private string _updateStatus = "Noch nicht geprueft.";
    public string UpdateStatus
    {
        get => _updateStatus;
        private set { _updateStatus = value; OnPropertyChanged(); }
    }

    public bool UpdateAvailable => _pendingUpdate is not null;

    #endregion

    #region Aktionen

    private async Task AnalyzeTestFileAsync()
    {
        var file = PickSrFile();
        if (file is null) return;

        StatusText = "Analysiere ...";
        try
        {
            var report = await Task.Run(() => _processor.AnalyzeAsync(file)).ConfigureAwait(true);
            _lastReport = report;
            AnalysisText = BuildAnalysisText(report);
            Log($"Analyse abgeschlossen: {report.Measurements.Count} Messwerte aus {Path.GetFileName(file)}.");
            StatusText = $"Analyse abgeschlossen: {report.Measurements.Count} Messwerte";
        }
        catch (Exception ex)
        {
            AnalysisText = $"Analyse fehlgeschlagen:{Environment.NewLine}{ex.Message}";
            Log($"Analyse fehlgeschlagen: {ex.Message}");
            StatusText = "Analyse fehlgeschlagen";
        }
    }

    private async Task CreateTestGdtAsync()
    {
        var file = PickSrFile();
        if (file is null) return;

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            MessageBox.Show("Bitte zuerst einen Ausgabeordner konfigurieren.", "DCMtoGDTReports");
            return;
        }

        StatusText = "Erzeuge GDT-Testdatei ...";
        try
        {
            var report = await Task.Run(() => _processor.AnalyzeAsync(file)).ConfigureAwait(true);
            _lastReport = report;
            var writer = new GdtFileWriter(_settings.Gdt, _settings.GdtTemplate);
            var path = writer.Write(report, OutputFolder);

            AnalysisText = BuildAnalysisText(report)
                + Environment.NewLine + Environment.NewLine
                + "--- GDT ---" + Environment.NewLine
                + writer.BuildContent(report).Replace("\r\n", Environment.NewLine);

            Log($"GDT-Testdatei erzeugt: {Path.GetFileName(path)}");
            StatusText = $"GDT-Testdatei erzeugt: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Log($"GDT-Erzeugung fehlgeschlagen: {ex.Message}");
            StatusText = "GDT-Erzeugung fehlgeschlagen";
        }
    }

    private void ToggleWatcher()
    {
        try
        {
            if (IsWatching)
            {
                _watcher?.Stop();
                _watcher?.Dispose();
                _watcher = null;
            }
            else
            {
                SaveSettings();
                _watcher = new FolderWatcherService(_settings, _processor, _loggerFactory.CreateLogger<FolderWatcherService>());
                _watcher.FileProcessed += OnFileProcessed;
                _watcher.Start();
            }
        }
        catch (Exception ex)
        {
            Log($"Ordnerueberwachung: {ex.Message}");
            MessageBox.Show(ex.Message, "Ordnerueberwachung", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        OnPropertyChanged(nameof(IsWatching));
        OnPropertyChanged(nameof(WatcherButtonText));
        OnPropertyChanged(nameof(WatcherStateText));
        RefreshDashboard();
    }

    private void OnFileProcessed(object? sender, ProcessingResult result)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            InsertRecentRow(new ProcessedFileRow
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                FileName = Path.GetFileName(result.SourceFilePath),
                Status = result.StatusText,
                GdtFile = Path.GetFileName(result.GdtFilePath ?? string.Empty),
                Info = result.ErrorMessage ?? string.Empty
            });
            RefreshCounters();
        });
    }

    private void SaveSettings()
    {
        var problems = SettingsService.Validate(_settings);
        if (problems.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, problems), "Konfiguration unvollstaendig",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settingsService.Save(_settings);
        SettingsService.EnsureFolders(_settings);

        // Registry und Verarbeitung mit den neuen Pfaden neu aufbauen.
        _registry = CreateRegistry();
        _processor = CreateProcessor();

        Log("Konfiguration gespeichert.");
        StatusText = "Konfiguration gespeichert";
        RefreshDashboard();
    }

    /// <summary>
    /// Richtet DCMTK ein. Der Download erfolgt ausschliesslich nach ausdruecklicher Bestaetigung.
    /// </summary>
    private async Task SetupDcmtkAsync()
    {
        var target = DcmtkLocator.GetBundledInstallDirectory();
        var answer = MessageBox.Show(
            "DCMTK ist optional - die Auswertung funktioniert bereits mit dem eingebauten DICOM-Toolkit." + Environment.NewLine + Environment.NewLine +
            "Soll DCMTK jetzt von dicom.offis.de heruntergeladen und lokal installiert werden?" + Environment.NewLine +
            $"Zielordner: {target}" + Environment.NewLine + Environment.NewLine +
            "Mit 'Nein' koennen Sie stattdessen einen vorhandenen DCMTK-Ordner auswaehlen.",
            "DCMTK einrichten", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel) return;

        if (answer == MessageBoxResult.No)
        {
            BrowseFolder(v => DcmtkPath = v, DcmtkPath);
            return;
        }

        StatusText = "Lade DCMTK ...";
        try
        {
            var installer = new DcmtkInstaller();
            var progress = new Progress<string>(Log);
            var installation = await installer
                .DownloadAndInstallAsync(DcmtkInstaller.DefaultDownloadUrl, target, progress: progress)
                .ConfigureAwait(true);

            DcmtkPath = installation.BinPath;
            _settings.Processing.PreferredEngine = SrExtractorFactory.EngineDcmtk;
            SaveSettings();
            Log($"DCMTK installiert: {installation.Dsr2XmlPath}");
        }
        catch (Exception ex)
        {
            Log($"DCMTK-Installation fehlgeschlagen: {ex.Message}");
            MessageBox.Show(
                ex.Message + Environment.NewLine + Environment.NewLine + DcmtkLocator.BuildNotFoundHint(),
                "DCMTK einrichten", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            RefreshDcmtkStatus();
            StatusText = "Bereit";
        }
    }

    #endregion

    #region Aktualisierung

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (!silent) UpdateStatus = "Suche nach Updates ...";

        var result = await new UpdateService(_settings.Update, logger: _loggerFactory.CreateLogger<UpdateService>())
            .CheckAsync().ConfigureAwait(true);

        _pendingUpdate = result.IsUpdateAvailable ? result.Manifest : null;
        UpdateStatus = result.Message;

        OnPropertyChanged(nameof(UpdateAvailable));
        InstallUpdateCommand.RaiseCanExecuteChanged();

        if (result.IsUpdateAvailable)
        {
            Log($"Update verfuegbar: {result.Manifest!.Version}");

            if (_settings.Update.InstallAutomatically)
            {
                await InstallUpdateAsync().ConfigureAwait(true);
                return;
            }

            if (result.Manifest.Mandatory && !silent)
            {
                MessageBox.Show(
                    $"Version {result.Manifest.Version} ist ein Pflichtupdate." + Environment.NewLine +
                    result.Manifest.Notes,
                    "Update verfuegbar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else if (!silent)
        {
            Log(result.Message);
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null) return;

        var confirm = MessageBox.Show(
            $"Version {_pendingUpdate.Version} installieren?" + Environment.NewLine + Environment.NewLine +
            (string.IsNullOrWhiteSpace(_pendingUpdate.Notes) ? string.Empty : _pendingUpdate.Notes + Environment.NewLine + Environment.NewLine) +
            "Die Anwendung wird dafuer beendet und anschliessend neu gestartet." +
            (string.IsNullOrWhiteSpace(_settings.Update.ServiceName)
                ? string.Empty
                : Environment.NewLine + $"Der Dienst '{_settings.Update.ServiceName}' wird kurz angehalten."),
            "Update installieren", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        UpdateStatus = "Update wird vorbereitet ...";
        try
        {
            var service = new UpdateService(_settings.Update, logger: _loggerFactory.CreateLogger<UpdateService>());
            var staging = await service
                .DownloadAndStageAsync(_pendingUpdate, new Progress<string>(Log))
                .ConfigureAwait(true);

            var installer = new UpdateInstaller(_loggerFactory.CreateLogger<UpdateInstaller>());
            var started = installer.Install(
                staging,
                restartExecutable: Environment.ProcessPath,
                serviceName: _settings.Update.ServiceName);

            if (!started)
            {
                UpdateStatus = "Das Updateskript konnte nicht gestartet werden.";
                return;
            }

            // Die Anwendung muss sich beenden, damit die Programmdateien freigegeben werden.
            Log("Anwendung wird fuer das Update beendet.");
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update fehlgeschlagen: {ex.Message}";
            Log(UpdateStatus);
            MessageBox.Show(ex.Message, "Update fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region GDT-Aufbau

    /// <summary>Oeffnet den Vorlagen-Editor. Als Vorschau dient die zuletzt analysierte Datei.</summary>
    private void EditTemplate()
    {
        var catalogService = CreateCatalogService();
        var dialog = new TemplateEditorWindow(_settings, catalogService.Load(), _lastReport)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        _settings.GdtTemplate = dialog.EditedTemplate;
        catalogService.Save(dialog.EditedCatalog);
        SaveSettings();

        var catalog = dialog.EditedCatalog;
        Log(catalog.Enabled
            ? $"Messwertauswahl aktiv: {catalog.AllEntries.Count(e => e.Selected)} von {catalog.AllEntries.Count()} Eintraegen."
            : "Messwertauswahl gespeichert, es werden weiterhin alle Werte uebernommen.");

        Log(_settings.GdtTemplate.Enabled
            ? $"Eigener GDT-Aufbau aktiv ({_settings.GdtTemplate.Lines.Count(l => l.Enabled)} aktive Zeilen)."
            : "GDT-Aufbau gespeichert, es wird weiter der Standardaufbau verwendet.");
    }

    /// <summary>
    /// Wertet mehrere SR-Dateien aus, um den Messwert-Katalog zu fuellen. Es wird nichts
    /// geschrieben - die Dateien werden nur gelesen und die gefundenen Typen gemerkt.
    /// </summary>
    private async Task LearnFromFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "SR-Dateien zum Lernen auswaehlen",
            Filter = "DICOM SR (SR*.dcm)|SR*.dcm|DICOM-Dateien (*.dcm)|*.dcm|Alle Dateien (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(InputFolder) ? InputFolder : null
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

        StatusText = $"Lerne aus {dialog.FileNames.Length} Datei(en) ...";
        var succeeded = 0;

        foreach (var file in dialog.FileNames)
        {
            try
            {
                // AnalyzeAsync ergaenzt den Katalog selbst und schreibt keine GDT-Datei.
                await Task.Run(() => _processor.AnalyzeAsync(file)).ConfigureAwait(true);
                succeeded++;
            }
            catch (Exception ex)
            {
                Log($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var catalog = CreateCatalogService().Load();
        StatusText = $"{succeeded} Datei(en) ausgewertet";
        Log($"Katalog aktualisiert: {catalog.Measurements.Count} Messgroessen, "
            + $"{catalog.Regions.Count} Regionen, {catalog.ImageModes.Count} Modi.");

        MessageBox.Show(
            $"Aus {succeeded} Datei(en) gelernt." + Environment.NewLine + Environment.NewLine
            + $"Bekannt sind jetzt {catalog.Measurements.Count} Messgroessen, {catalog.Regions.Count} Regionen "
            + $"und {catalog.ImageModes.Count} Aufnahmemodi." + Environment.NewLine + Environment.NewLine
            + "Die Auswahl treffen Sie unter \"GDT-Aufbau bearbeiten\".",
            "Messwerte gelernt", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private MeasurementCatalogService CreateCatalogService()
    {
        if (string.IsNullOrWhiteSpace(_settings.MeasurementCatalogPath))
            _settingsService.ApplyFallbacks(_settings);

        return new MeasurementCatalogService(_settings.MeasurementCatalogPath);
    }

    /// <summary>
    /// Setzt eine kompakte Befundausgabe: Wiederholungsmessungen werden zusammengefasst und
    /// die 18 Strain-Einzelsegmente entfallen. Der globale Strain-Wert bleibt erhalten.
    /// </summary>
    private void ApplyCompactProfile()
    {
        FilterEnabled = true;
        FilterRepeatedValues = RepeatedValueMode.MinMaxMean;
        FilterExcludeFindingSites = "*segment";
        FilterOnlyMapped = false;
        FilterOnlySelected = false;
        FilterMaxMeasurements = 0;

        SaveSettings();
        Log("Kompakt-Vorgabe gesetzt: Wiederholungen zusammengefasst, Strain-Einzelsegmente ausgeblendet.");
    }

    #endregion

    #region Hilfsfunktionen

    private SqliteProcessedFileRegistry CreateRegistry()
    {
        var registry = new SqliteProcessedFileRegistry(_settings.RegistryDatabasePath);
        registry.Initialize();
        return registry;
    }

    private SrFileProcessor CreateProcessor()
        => new(_settings, _registry, _loggerFactory.CreateLogger<SrFileProcessor>());

    private void RefreshDcmtkStatus()
    {
        var installation = DcmtkLocator.Locate(_settings.DcmtkPath);
        DcmtkAvailable = installation is not null;
        DcmtkStatus = installation is null
            ? "Nicht gefunden - es wird das eingebaute DICOM-Toolkit verwendet."
            : $"Gefunden ({installation.Source}): {installation.Dsr2XmlPath}";
    }

    public void RefreshDashboard()
    {
        RefreshCounters();

        RecentFiles.Clear();
        foreach (var entry in _registry.GetRecent(50))
        {
            RecentFiles.Add(new ProcessedFileRow
            {
                Time = entry.ProcessedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                FileName = entry.FileName,
                Status = MapStatus(entry.Status),
                GdtFile = Path.GetFileName(entry.CreatedGdtFile),
                Info = entry.ErrorMessage
            });
        }
    }

    private void RefreshCounters()
    {
        SuccessCount = _registry.CountByStatus(ProcessedFileEntry.StatusSuccess);
        FailedCount = _registry.CountByStatus(ProcessedFileEntry.StatusFailed);
        SkippedCount = _registry.CountByStatus(ProcessedFileEntry.StatusSkipped);
        PendingCount = Directory.Exists(InputFolder)
            ? Directory.EnumerateFiles(InputFolder, FilePattern).Count()
            : 0;
    }

    private static string MapStatus(string status) => status switch
    {
        ProcessedFileEntry.StatusSuccess => "Neu verarbeitet",
        ProcessedFileEntry.StatusSkipped => "Uebersprungen",
        ProcessedFileEntry.StatusFailed => "Fehler",
        _ => status
    };

    private void InsertRecentRow(ProcessedFileRow row)
    {
        RecentFiles.Insert(0, row);
        while (RecentFiles.Count > 100) RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }

    private static string BuildAnalysisText(SrReport report)
    {
        var header = report.Header;
        var lines = new List<string>
        {
            $"Engine         : {report.Engine}",
            $"Dokument       : {header.DocumentTitle}",
            $"Geraet         : {header.DeviceDescription} ({header.StationName})",
            $"Patient-Nr     : {header.PatientId}",
            $"Untersuchung   : {Core.Dicom.DicomValueConverter.ToDisplayDate(header.StudyDate)} {Core.Dicom.DicomValueConverter.ToDisplayTime(header.StudyTime)}",
            $"Accession      : {header.AccessionNumber}",
            $"SOPInstanceUID : {header.SopInstanceUid}",
            $"Messwerte      : {report.Measurements.Count} (aus {report.RawMeasurementCount} SR-Knoten)",
            $"Filter         : {(report.FilteredOutCount > 0 ? $"{report.FilteredOutCount} Werte entfallen" : "keine Reduktion")}",
            string.Empty
        };

        foreach (var group in report.Measurements.GroupBy(m => m.Group))
        {
            lines.Add($"[{group.Key}]");
            lines.AddRange(group.Select(m => "  " + m.ToDisplayLine()));
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string? PickSrFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "DICOM Structured Report auswaehlen",
            Filter = "DICOM SR (SR*.dcm)|SR*.dcm|DICOM-Dateien (*.dcm)|*.dcm|Alle Dateien (*.*)|*.*",
            InitialDirectory = Directory.Exists(InputFolder) ? InputFolder : null
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void BrowseFolder(Action<string> setter, string? current)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ordner auswaehlen",
            InitialDirectory = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current) ? current : null
        };

        if (dialog.ShowDialog() == true) setter(dialog.FolderName);
    }

    private static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Log(string message) =>
        AppendLog(new LogEntry(DateTimeOffset.Now, LogLevel.Information, nameof(MainViewModel), message));

    private void AppendLog(LogEntry entry)
    {
        LogText += entry + Environment.NewLine;

        // Loganzeige begrenzen, damit die GUI bei Dauerbetrieb nicht vollaeuft.
        if (LogText.Length > 200_000)
            LogText = LogText[^100_000..];
    }

    public void Shutdown()
    {
        _watcher?.Stop();
        _watcher?.Dispose();
        _loggerFactory.Dispose();
    }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
