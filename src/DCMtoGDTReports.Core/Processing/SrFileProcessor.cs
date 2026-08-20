using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Filtering;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Mapping;
using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Core.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DCMtoGDTReports.Core.Processing;

/// <summary>
/// Verarbeitet genau eine SR-Datei: Dublettenpruefung, Auswertung, GDT-Erzeugung, Archivierung.
/// Die Originaldatei wird nicht angefasst - es wird immer mit einer temporaeren Kopie gearbeitet,
/// die anschliessend geloescht wird.
/// </summary>
public sealed class SrFileProcessor(
    AppSettings settings,
    IProcessedFileRegistry registry,
    ILogger<SrFileProcessor>? logger = null)
{
    private readonly AppSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IProcessedFileRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly ILogger _logger = logger ?? NullLogger<SrFileProcessor>.Instance;
    private readonly MeasurementMapper _mapper = new(settings.MeasurementShortNames, settings.MethodShortNames);
    private readonly GdtFileWriter _gdtWriter = new(settings.Gdt, settings.GdtTemplate);
    private readonly MeasurementCatalogService _catalogService = new(
        string.IsNullOrWhiteSpace(settings.MeasurementCatalogPath)
            ? Path.Combine(Path.GetTempPath(), "DCMtoGDTReports-catalog.json")
            : settings.MeasurementCatalogPath);

    private readonly DicomForwarder _forwarder = new(
        settings.Processing.ForwardFolder,
        logger ?? NullLogger<SrFileProcessor>.Instance);

    /// <summary>Ist eine Weiterleitung an das PVS konfiguriert?</summary>
    public bool ForwardsToPvs => _forwarder.IsConfigured;

    /// <summary>
    /// Reicht eine Datei unveraendert an das PVS weiter, ohne sie auszuwerten.
    /// Wird fuer Bilder und Loops gebraucht, die nicht auf das Verarbeitungsmuster passen.
    /// </summary>
    public bool Forward(string sourceFilePath) => _forwarder.Forward(sourceFilePath);

    /// <summary>Wertet eine SR-Datei aus, ohne etwas zu schreiben (Button "Testdatei analysieren").</summary>
    public async Task<SrReport> AnalyzeAsync(string sourceFilePath, string? debugXmlPath = null, CancellationToken ct = default)
    {
        var extractor = SrExtractorFactory.Create(_settings, out _);
        var workingCopy = CreateTemporaryCopy(sourceFilePath);
        try
        {
            var report = await extractor.ExtractAsync(workingCopy, debugXmlPath, ct).ConfigureAwait(false);
            PrepareMeasurements(report);
            return report;
        }
        finally
        {
            TryDeleteTemporary(workingCopy);
        }
    }

    /// <summary>
    /// Kurznamen und Zahlenformat anwenden, aus dem Bericht lernen, filtern und zum Schluss
    /// die deutschen Bezeichnungen setzen. Die Reihenfolge ist wichtig: Katalog und Filter
    /// arbeiten mit den DICOM-Originalbezeichnungen, erst die Ausgabe wird uebersetzt.
    /// </summary>
    private void PrepareMeasurements(SrReport report)
    {
        _mapper.Apply(report.Measurements, _settings.Gdt);

        var catalog = LearnFromReport(report);
        var filter = new MeasurementFilter(
            _settings.MeasurementFilter,
            _settings.Gdt,
            CreateMapper(),
            catalog);

        report.ApplyFilteredMeasurements(filter.Apply(report.Measurements));
        CreateMapper(catalog).ApplyGermanNames(report.Measurements);
    }

    /// <summary>
    /// Baut den Mapper aus den Einstellungen und - falls vorhanden - den im Katalog
    /// hinterlegten eigenen Bezeichnungen. Diese haben Vorrang vor den Vorgaben.
    /// </summary>
    private MeasurementMapper CreateMapper(MeasurementCatalog? catalog = null)
    {
        var measurementNames = Combine(_settings.MeasurementShortNames,
            catalog is null ? null : MeasurementCatalogService.GetCustomNames(catalog, CatalogEntryKind.Measurement));
        var regionNames = Combine(_settings.RegionNames,
            catalog is null ? null : MeasurementCatalogService.GetCustomNames(catalog, CatalogEntryKind.Region));
        var imageModeNames = Combine(_settings.ImageModeNames,
            catalog is null ? null : MeasurementCatalogService.GetCustomNames(catalog, CatalogEntryKind.ImageMode));

        return new MeasurementMapper(measurementNames, _settings.MethodShortNames, regionNames, imageModeNames);
    }

    private static Dictionary<string, string> Combine(
        IReadOnlyDictionary<string, string>? settings, IReadOnlyDictionary<string, string>? catalog)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (settings is not null)
            foreach (var (key, value) in settings) result[key] = value;

        // Der Katalog wird zuletzt angewendet und gewinnt damit gegen die Konfiguration.
        if (catalog is not null)
            foreach (var (key, value) in catalog) result[key] = value;

        return result;
    }

    /// <summary>
    /// Ergaenzt den Katalog um neu gesehene Messgroessen, Regionen und Aufnahmemodi.
    /// Neue Eintraege sind immer ausgewaehlt, damit nichts unbemerkt wegfaellt.
    /// </summary>
    private MeasurementCatalog LearnFromReport(SrReport report)
    {
        try
        {
            var catalog = _catalogService.Load();
            var added = MeasurementCatalogService.Learn(catalog, report);
            _catalogService.Save(catalog);

            if (added > 0)
                _logger.LogInformation("{Count} neue Messwert-Typen in den Katalog aufgenommen.", added);

            return catalog;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Der Katalog ist eine Komfortfunktion - die Verarbeitung laeuft auch ohne ihn weiter.
            _logger.LogWarning(ex, "Messwert-Katalog konnte nicht aktualisiert werden.");
            return new MeasurementCatalog();
        }
    }

    /// <summary>Vollstaendige Verarbeitung inklusive GDT-Erzeugung und Registrierung.</summary>
    public async Task<ProcessingResult> ProcessAsync(string sourceFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            return ProcessingResult.Failure(sourceFilePath, "Datei existiert nicht (mehr).");

        var fileName = Path.GetFileName(sourceFilePath);
        string? sha256 = null;
        string? workingCopy = null;

        // Die Weiterleitung an das PVS darf nicht an unserer Auswertung haengen: auch eine
        // Dublette oder eine fehlerhafte Datei muss beim PVS ankommen.
        var forwardWhenDone = true;

        try
        {
            sha256 = await FileHasher.ComputeSha256Async(sourceFilePath, ct).ConfigureAwait(false);

            if (_settings.Processing.DuplicateBySha256)
            {
                var known = _registry.FindSuccessful(sha256, null);
                if (known is not null)
                {
                    _logger.LogInformation("Datei {File} wurde bereits verarbeitet (SHA256-Treffer).", fileName);
                    return ProcessingResult.Duplicate(sourceFilePath, known.CreatedGdtFile, sha256);
                }
            }

            workingCopy = CreateTemporaryCopy(sourceFilePath);

            var extractor = SrExtractorFactory.Create(_settings, out _);
            var debugXmlPath = BuildDebugXmlPath(fileName);
            var report = await extractor.ExtractAsync(workingCopy, debugXmlPath, ct).ConfigureAwait(false);
            PrepareMeasurements(report);

            if (_settings.Processing.DuplicateBySopInstanceUid && !string.IsNullOrWhiteSpace(report.Header.SopInstanceUid))
            {
                var known = _registry.FindSuccessful(null, report.Header.SopInstanceUid);
                if (known is not null)
                {
                    _logger.LogInformation("Datei {File} wurde bereits verarbeitet (SOPInstanceUID-Treffer).", fileName);
                    return ProcessingResult.Duplicate(sourceFilePath, known.CreatedGdtFile, sha256);
                }
            }

            if (report.Measurements.Count == 0)
            {
                var reason = report.FilteredOutCount > 0
                    ? "Der konfigurierte Messwertfilter hat alle Messwerte entfernt."
                    : "Keine numerischen Messwerte im Structured Report.";
                _logger.LogWarning("Datei {File} liefert keine uebertragbaren Messwerte: {Reason}", fileName, reason);
                Register(sourceFilePath, sha256, report, string.Empty, ProcessedFileEntry.StatusSkipped, reason);
                return ProcessingResult.Skip(sourceFilePath, reason, sha256);
            }

            var gdtPath = _gdtWriter.Write(report, _settings.OutputFolder);
            _logger.LogInformation(
                "GDT-Datei {Gdt} erzeugt: {Count} Messwerte (von {Raw} SR-Knoten, {Filtered} durch Filter entfallen), Engine {Engine}.",
                Path.GetFileName(gdtPath), report.Measurements.Count, report.RawMeasurementCount,
                report.FilteredOutCount, report.Engine);

            ArchiveSource(sourceFilePath, report);
            Register(sourceFilePath, sha256, report, gdtPath, ProcessedFileEntry.StatusSuccess, string.Empty);

            return ProcessingResult.Success(sourceFilePath, gdtPath, report, sha256);
        }
        catch (OperationCanceledException)
        {
            forwardWhenDone = false;
            throw;
        }
        catch (Exception ex)
        {
            // Absichtlich breit: eine defekte Datei darf die Ordnerueberwachung nicht stoppen.
            _logger.LogError(ex, "Verarbeitung von {File} fehlgeschlagen.", fileName);
            HandleError(sourceFilePath, sha256, ex);
            return ProcessingResult.Failure(sourceFilePath, ex.Message, sha256);
        }
        finally
        {
            if (workingCopy is not null) TryDeleteTemporary(workingCopy);
            if (forwardWhenDone) _forwarder.Forward(sourceFilePath);
        }
    }

    /// <summary>Erstellt eine temporaere Arbeitskopie, damit die Originaldatei unberuehrt bleibt.</summary>
    private static string CreateTemporaryCopy(string sourceFilePath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DCMtoGDTReports");
        Directory.CreateDirectory(tempDirectory);

        var target = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.dcm");
        File.Copy(sourceFilePath, target, overwrite: true);
        return target;
    }

    private void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Temporaere Arbeitskopie konnte nicht geloescht werden.");
        }
    }

    private string? BuildDebugXmlPath(string sourceFileName)
    {
        if (!_settings.Processing.KeepXmlDebugFiles || string.IsNullOrWhiteSpace(_settings.ArchiveFolder))
            return null;

        Directory.CreateDirectory(_settings.ArchiveFolder);
        var name = Path.GetFileNameWithoutExtension(sourceFileName);
        return Path.Combine(_settings.ArchiveFolder, GdtFileWriter.Sanitize($"{name}_{DateTime.Now:yyyyMMddHHmmss}.xml"));
    }

    /// <summary>Kopiert bzw. verschiebt die Originaldatei ins Archiv, je nach Konfiguration.</summary>
    private void ArchiveSource(string sourceFilePath, SrReport report)
    {
        if (string.IsNullOrWhiteSpace(_settings.ArchiveFolder)) return;
        if (!_settings.Processing.CopyToArchive && !_settings.Processing.MoveProcessedFiles) return;

        try
        {
            Directory.CreateDirectory(_settings.ArchiveFolder);
            var targetName = GdtFileWriter.Sanitize(
                $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{report.Header.AccessionNumber}_{DateTime.Now:yyyyMMddHHmmss}.dcm");
            var target = Path.Combine(_settings.ArchiveFolder, targetName);

            // Bei aktiver Weiterleitung nur kopieren - verschoben wird die Datei anschliessend
            // an das PVS, sonst waere sie hier schon weg.
            if (_settings.Processing.MoveProcessedFiles && !_forwarder.IsConfigured)
                File.Move(sourceFilePath, target, overwrite: true);
            else
                File.Copy(sourceFilePath, target, overwrite: true);
        }
        catch (IOException ex)
        {
            // Archivierung ist nachrangig - die GDT-Datei wurde bereits erfolgreich geschrieben.
            _logger.LogWarning(ex, "Archivierung von {File} fehlgeschlagen.", Path.GetFileName(sourceFilePath));
        }
    }

    private void HandleError(string sourceFilePath, string? sha256, Exception exception)
    {
        if (_settings.Processing.CopyFailedFilesToErrorFolder && !string.IsNullOrWhiteSpace(_settings.ErrorFolder))
        {
            try
            {
                Directory.CreateDirectory(_settings.ErrorFolder);
                var target = Path.Combine(_settings.ErrorFolder, GdtFileWriter.Sanitize(
                    $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_{DateTime.Now:yyyyMMddHHmmss}.dcm"));
                if (File.Exists(sourceFilePath)) File.Copy(sourceFilePath, target, overwrite: true);

                File.WriteAllText(Path.ChangeExtension(target, ".error.txt"),
                    $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Fehlerdatei konnte nicht abgelegt werden.");
            }
        }

        try
        {
            Register(sourceFilePath, sha256, null, string.Empty, ProcessedFileEntry.StatusFailed, exception.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehlerhafter Verarbeitungsversuch konnte nicht registriert werden.");
        }
    }

    private void Register(string sourceFilePath, string? sha256, SrReport? report, string gdtPath, string status, string error)
    {
        var info = new FileInfo(sourceFilePath);
        _registry.Add(new ProcessedFileEntry
        {
            FilePath = sourceFilePath,
            FileName = Path.GetFileName(sourceFilePath),
            FileSize = info.Exists ? info.Length : 0,
            LastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.UtcNow,
            Sha256 = sha256 ?? string.Empty,
            SopInstanceUid = report?.Header.SopInstanceUid ?? string.Empty,
            StudyInstanceUid = report?.Header.StudyInstanceUid ?? string.Empty,
            AccessionNumber = report?.Header.AccessionNumber ?? string.Empty,
            PatientId = report?.Header.PatientId ?? string.Empty,
            CreatedGdtFile = gdtPath,
            ProcessedAtUtc = DateTime.UtcNow,
            Status = status,
            ErrorMessage = error
        });
    }
}
