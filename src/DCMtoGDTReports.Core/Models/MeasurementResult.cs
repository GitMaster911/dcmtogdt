namespace DCMtoGDTReports.Core.Models;

/// <summary>
/// Ein einzelner numerischer Messwert aus dem DICOM Structured Report.
/// </summary>
public sealed class MeasurementResult
{
    /// <summary>Code Meaning des Concept Name, z. B. "Left Ventricle Internal End Diastolic Dimension".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gemappte Kurzbezeichnung, z. B. "LVIDd". Faellt auf <see cref="Name"/> zurueck.</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>Formatierter Zahlenwert (bereits gerundet, Anzeigeformat).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Einheit als Code Meaning bzw. UCUM-Kuerzel, z. B. "cm".</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Finding Site (SRT:G-C0E3), z. B. "Left Ventricle".</summary>
    public string FindingSite { get; set; } = string.Empty;

    /// <summary>Measurement Method (SRT:G-C036), z. B. "Teichholz".</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Bezeichnung der Measurement Group inkl. Image Mode, z. B. "Left Ventricle / 2D mode".</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Concept-Name-Code in der Form "SCHEMA:CODE", z. B. "LN:29436-3".</summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>Pfad im SR-Baum, z. B. "/1/2/5" - dient der Nachvollziehbarkeit.</summary>
    public string RawPath { get; set; } = string.Empty;

    /// <summary>Image Mode (SRT:G-0373), z. B. "2D mode" oder "Doppler Pulsed".</summary>
    public string ImageMode { get; set; } = string.Empty;

    /// <summary>Cardiac Cycle Point (SRT:R-4089A), z. B. "Systole".</summary>
    public string CardiacCyclePoint { get; set; } = string.Empty;

    /// <summary>Direction of Flow (SRT:G-C048), z. B. "Regurgitant Flow".</summary>
    public string DirectionOfFlow { get; set; } = string.Empty;

    /// <summary>Derivation (DCM:121401), z. B. "Mean".</summary>
    public string Derivation { get; set; } = string.Empty;

    /// <summary>
    /// Selection Status (DCM:121404). Der Vivid T8 liefert jeden Messwert doppelt: einmal mit und
    /// einmal ohne Selection Status. Das gesetzte Feld markiert den vom Geraet gewaehlten Wert.
    /// </summary>
    public string SelectionStatus { get; set; } = string.Empty;

    /// <summary>Unveraenderter numerischer Rohwert aus dem SR.</summary>
    public string RawValue { get; set; } = string.Empty;

    /// <summary>Numerischer Wert, falls parsebar.</summary>
    public double? NumericValue { get; set; }

    /// <summary>
    /// Hinweis auf eine Zusammenfassung mehrerer Einzelmessungen, z. B. "Mittel aus 6".
    /// Wird vom Messwertfilter gesetzt und im Ergebnistext mit ausgegeben.
    /// </summary>
    public string AggregationNote { get; set; } = string.Empty;

    /// <summary>Anzeigezeile fuer den GDT-Ergebnistext.</summary>
    public string ToDisplayLine()
    {
        var label = string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName;
        var qualifiers = new List<string>();
        if (!string.IsNullOrWhiteSpace(Method)) qualifiers.Add(Method);
        if (!string.IsNullOrWhiteSpace(DirectionOfFlow)) qualifiers.Add(DirectionOfFlow);
        if (!string.IsNullOrWhiteSpace(AggregationNote)) qualifiers.Add(AggregationNote);

        var suffix = qualifiers.Count > 0 ? $" ({string.Join(", ", qualifiers)})" : string.Empty;
        var unit = string.IsNullOrWhiteSpace(Unit) ? string.Empty : " " + Unit;
        return $"{label}{suffix}: {Value}{unit}";
    }

    /// <summary>Flache Kopie, damit der Filter aggregierte Werte bilden kann, ohne das Original zu veraendern.</summary>
    public MeasurementResult Clone() => (MeasurementResult)MemberwiseClone();
}
