namespace DCMtoGDTReports.Core.Models;

/// <summary>
/// Die aus dem DICOM-Header extrahierten Kopfdaten einer SR-Datei.
/// </summary>
public sealed class SrHeader
{
    public string PatientId { get; set; } = string.Empty;

    /// <summary>Rohwert im DICOM-PN-Format, z. B. "Mustermann^Max".</summary>
    public string PatientName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;

    /// <summary>DICOM DA, Format YYYYMMDD.</summary>
    public string PatientBirthDate { get; set; } = string.Empty;

    /// <summary>DICOM CS: M, F, O.</summary>
    public string PatientSex { get; set; } = string.Empty;

    /// <summary>DICOM DA, Format YYYYMMDD.</summary>
    public string StudyDate { get; set; } = string.Empty;

    /// <summary>DICOM TM, Format HHMMSS(.FFFFFF).</summary>
    public string StudyTime { get; set; } = string.Empty;

    public string AccessionNumber { get; set; } = string.Empty;
    public string StudyInstanceUid { get; set; } = string.Empty;
    public string SeriesInstanceUid { get; set; } = string.Empty;
    public string SopInstanceUid { get; set; } = string.Empty;
    public string StudyDescription { get; set; } = string.Empty;
    public string SeriesDescription { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string ManufacturerModelName { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;

    /// <summary>Titel des SR-Wurzelcontainers, z. B. "Adult Echocardiography Procedure Report".</summary>
    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary>Geraetebeschreibung fuer die Kopfzeile des Ergebnistexts.</summary>
    public string DeviceDescription =>
        string.Join(" ", new[] { Manufacturer, ManufacturerModelName }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
}

/// <summary>
/// Das vollstaendig ausgewertete Ergebnis einer SR-Datei.
/// </summary>
public sealed class SrReport
{
    public required SrHeader Header { get; init; }

    /// <summary>Die Messwerte, die in die GDT-Datei uebernommen werden.</summary>
    public required IReadOnlyList<MeasurementResult> Measurements { get; set; }

    /// <summary>Welche Engine die Daten geliefert hat (eingebautes Toolkit oder DCMTK).</summary>
    public required string Engine { get; init; }

    /// <summary>Pfad einer optional gespeicherten Debug-XML-Datei.</summary>
    public string? DebugXmlPath { get; set; }

    /// <summary>Anzahl der NUM-Knoten vor der Dublettenbereinigung.</summary>
    public int RawMeasurementCount { get; set; }

    /// <summary>Anzahl der vom Messwertfilter entfernten bzw. zusammengefassten Werte.</summary>
    public int FilteredOutCount { get; private set; }

    /// <summary>Klartext, welche Filterstufe wie viele Messwerte entfernt hat.</summary>
    public string FilterSummary { get; set; } = string.Empty;

    /// <summary>
    /// Alle Messwerte vor der Filterung. Wird fuer die Vorschau im GDT-Editor gebraucht,
    /// damit dort sichtbar ist, was die Auswahl tatsaechlich bewirkt.
    /// </summary>
    public IReadOnlyList<MeasurementResult> AllMeasurements { get; private set; } = [];

    /// <summary>Uebernimmt das Ergebnis des Messwertfilters und merkt sich, wie viel entfallen ist.</summary>
    public void ApplyFilteredMeasurements(IReadOnlyList<MeasurementResult> filtered)
    {
        ArgumentNullException.ThrowIfNull(filtered);

        if (AllMeasurements.Count == 0) AllMeasurements = Measurements;

        FilteredOutCount = Math.Max(0, Measurements.Count - filtered.Count);
        Measurements = filtered;
    }
}
