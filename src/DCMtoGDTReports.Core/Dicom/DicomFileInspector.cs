using FellowOakDicom;

namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Erkennt anhand des DICOM-Inhalts, ob eine Datei ein Structured Report ist.
///
/// Die Erkennung darf nicht am Dateinamen haengen: welche Namen im Eingangsordner ankommen,
/// bestimmt der DICOM-Speicherdienst (storescp) und nicht das Geraet. Je nach Aufrufparametern
/// heissen dieselben Daten "SRc.1.2.840...dcm", "SR.1.2.840..." oder auch nur "1.2.840...".
/// </summary>
public static class DicomFileInspector
{
    /// <summary>Alle SR-Storage-Klassen liegen unterhalb dieses UID-Zweigs.</summary>
    private const string StructuredReportSopClassPrefix = "1.2.840.10008.5.1.4.1.1.88.";

    /// <summary>
    /// Prueft, ob die Datei ein Structured Report ist. Gelesen wird nur der Kopf,
    /// grosse Datenelemente (Bilddaten) werden uebersprungen.
    /// </summary>
    public static bool IsStructuredReport(string filePath)
    {
        try
        {
            var dataset = DicomFile.Open(filePath, FileReadOption.SkipLargeTags).Dataset;

            if (dataset.GetSingleValueOrDefault(DicomTag.SOPClassUID, string.Empty)
                    .StartsWith(StructuredReportSopClassPrefix, StringComparison.Ordinal))
                return true;

            return string.Equals(
                dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty), "SR",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is DicomFileException or IOException or UnauthorizedAccessException)
        {
            // Keine lesbare DICOM-Datei - dann ist es auch kein SR.
            return false;
        }
    }

    /// <summary>Liest die SOPInstanceUID, ohne die Datei vollstaendig auszuwerten.</summary>
    public static string ReadSopInstanceUid(string filePath)
    {
        try
        {
            return DicomFile.Open(filePath, FileReadOption.SkipLargeTags)
                .Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty);
        }
        catch (Exception ex) when (ex is DicomFileException or IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
