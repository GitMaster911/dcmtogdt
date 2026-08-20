using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Auswertung einer DICOM-SR-Datei. Es gibt zwei Implementierungen:
/// das eingebaute Toolkit (fo-dicom) und optional DCMTK ueber dsr2xml.
/// </summary>
public interface ISrExtractor
{
    /// <summary>Anzeigename der Engine, z. B. "Builtin (fo-dicom)".</summary>
    string EngineName { get; }

    /// <summary>Liest Kopfdaten und Messwerte aus der SR-Datei.</summary>
    /// <param name="dicomFilePath">Pfad zur SR-Datei (in der Regel eine temporaere Kopie).</param>
    /// <param name="debugXmlPath">Optionaler Zielpfad fuer die Struktur-XML zur Fehlersuche.</param>
    Task<SrReport> ExtractAsync(string dicomFilePath, string? debugXmlPath = null, CancellationToken ct = default);
}
