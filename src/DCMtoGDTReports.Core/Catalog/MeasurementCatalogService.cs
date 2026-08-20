using System.Text.Json;
using System.Text.Json.Serialization;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Catalog;

/// <summary>
/// Laedt und speichert den Messwert-Katalog und lernt aus ausgewerteten SR-Dateien,
/// welche Messgroessen, Regionen und Aufnahmemodi das Geraet tatsaechlich liefert.
/// </summary>
public sealed class MeasurementCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _gate = new();

    public MeasurementCatalogService(string catalogFilePath)
    {
        if (string.IsNullOrWhiteSpace(catalogFilePath))
            throw new ArgumentException("Pfad zur Katalogdatei fehlt.", nameof(catalogFilePath));

        CatalogFilePath = catalogFilePath;
    }

    public string CatalogFilePath { get; }

    public MeasurementCatalog Load()
    {
        lock (_gate)
        {
            if (!File.Exists(CatalogFilePath)) return new MeasurementCatalog();

            try
            {
                return JsonSerializer.Deserialize<MeasurementCatalog>(File.ReadAllText(CatalogFilePath), JsonOptions)
                       ?? new MeasurementCatalog();
            }
            catch (JsonException)
            {
                // Ein beschaedigter Katalog darf die Verarbeitung nicht blockieren.
                return new MeasurementCatalog();
            }
        }
    }

    public void Save(MeasurementCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(CatalogFilePath))!);

            var tempPath = CatalogFilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(catalog, JsonOptions));
            File.Move(tempPath, CatalogFilePath, overwrite: true);
        }
    }

    /// <summary>
    /// Ergaenzt den Katalog um alles, was in diesem Bericht vorkommt.
    /// Liefert die Anzahl der neu hinzugekommenen Eintraege.
    /// </summary>
    public static int Learn(MeasurementCatalog catalog, SrReport report)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(report);

        var added = 0;
        foreach (var measurement in report.Measurements)
        {
            added += Touch(catalog.Measurements, CatalogEntryKind.Measurement,
                MeasurementKey(measurement), measurement.Name, measurement.ShortName, measurement.Unit);

            if (!string.IsNullOrWhiteSpace(measurement.FindingSite))
                added += Touch(catalog.Regions, CatalogEntryKind.Region,
                    measurement.FindingSite, measurement.FindingSite, string.Empty, string.Empty);

            if (!string.IsNullOrWhiteSpace(measurement.ImageMode))
                added += Touch(catalog.ImageModes, CatalogEntryKind.ImageMode,
                    measurement.ImageMode, measurement.ImageMode, string.Empty, string.Empty);
        }

        catalog.LastLearnedUtc = DateTime.UtcNow;
        catalog.LearnedFileCount++;

        Sort(catalog);
        return added;
    }

    /// <summary>Schluessel einer Messgroesse: bevorzugt der Concept-Code, sonst der Name.</summary>
    public static string MeasurementKey(MeasurementResult measurement)
        => string.IsNullOrWhiteSpace(measurement.SourceCode) ? measurement.Name : measurement.SourceCode;

    private static int Touch(
        List<CatalogEntry> entries, CatalogEntryKind kind,
        string key, string displayName, string shortName, string unit)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;

        var existing = entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            entries.Add(new CatalogEntry
            {
                Key = key,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName,
                ShortName = shortName,
                Kind = kind,
                Unit = unit,
                Selected = true,
                SeenCount = 1
            });
            return 1;
        }

        existing.SeenCount++;
        existing.LastSeenUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(shortName)) existing.ShortName = shortName;
        if (!string.IsNullOrWhiteSpace(unit)) existing.Unit = unit;
        if (!string.IsNullOrWhiteSpace(displayName)) existing.DisplayName = displayName;
        return 0;
    }

    private static void Sort(MeasurementCatalog catalog)
    {
        catalog.Measurements.Sort(CompareByLabel);
        catalog.Regions.Sort(CompareByLabel);
        catalog.ImageModes.Sort(CompareByLabel);
    }

    private static int CompareByLabel(CatalogEntry a, CatalogEntry b)
        => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
}
