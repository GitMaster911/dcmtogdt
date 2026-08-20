using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Tools;

namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Auswertung ueber DCMTK: dsr2xml erzeugt eine XML-Datei, die anschliessend geparst wird.
/// Kommt nur zum Einsatz, wenn DCMTK konfiguriert bzw. gefunden wurde.
/// </summary>
public sealed class DcmtkSrExtractor(DcmtkInstallation installation) : ISrExtractor
{
    private readonly DcmtkRunner _runner = new(installation);

    public string EngineName => "DCMTK (dsr2xml)";

    public async Task<SrReport> ExtractAsync(string dicomFilePath, string? debugXmlPath = null, CancellationToken ct = default)
    {
        // Ohne Debug-Pfad wird in einen temporaeren Ordner geschrieben und danach aufgeraeumt.
        var isTemporary = string.IsNullOrWhiteSpace(debugXmlPath);
        var xmlPath = isTemporary
            ? Path.Combine(Path.GetTempPath(), $"dsr2xml-{Guid.NewGuid():N}.xml")
            : debugXmlPath!;

        try
        {
            await _runner.ConvertSrToXmlAsync(dicomFilePath, xmlPath, ct).ConfigureAwait(false);
            var report = DsrXmlParser.Parse(xmlPath, EngineName);
            report.DebugXmlPath = isTemporary ? null : xmlPath;
            return report;
        }
        finally
        {
            if (isTemporary && File.Exists(xmlPath))
            {
                try { File.Delete(xmlPath); } catch (IOException) { /* Temp-Datei ist unkritisch */ }
            }
        }
    }
}
