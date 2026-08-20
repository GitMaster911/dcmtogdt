using System.Globalization;
using System.Text;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Gdt;

/// <summary>
/// Schreibt GDT-Dateien atomar: erst als .tmp, danach Umbenennung auf .gdt.
/// So sieht der GDT-Autoimport von MEDICAL OFFICE niemals eine halb geschriebene Datei.
/// </summary>
public sealed class GdtFileWriter
{
    static GdtFileWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private readonly GdtSettings _settings;
    private readonly Gdt6310Builder _builder;

    public GdtFileWriter(GdtSettings settings, Templates.GdtTemplate? template = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _builder = new Gdt6310Builder(settings) { Template = template };
        Encoding = ResolveEncoding(settings.EncodingCodePage);
    }

    public Encoding Encoding { get; }

    /// <summary>Erzeugt den GDT-Satz als Text.</summary>
    public string BuildContent(SrReport report) => _builder.BuildRecord(report, Encoding);

    /// <summary>Schreibt die GDT-Datei und liefert den endgueltigen Pfad zurueck.</summary>
    public string Write(SrReport report, string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new ArgumentException("Ausgabeordner ist nicht konfiguriert.", nameof(outputFolder));

        Directory.CreateDirectory(outputFolder);

        var fileName = BuildFileName(report);
        var targetPath = MakeUnique(Path.Combine(outputFolder, fileName));
        var tempPath = targetPath + ".tmp";

        var content = BuildContent(report);
        File.WriteAllBytes(tempPath, Encoding.GetBytes(content));
        File.Move(tempPath, targetPath, overwrite: false);

        return targetPath;
    }

    /// <summary>Loest die Platzhalter des konfigurierten Dateinamensmusters auf.</summary>
    public string BuildFileName(SrReport report)
    {
        var pattern = string.IsNullOrWhiteSpace(_settings.FileNamePattern)
            ? "{sender}{receiver}_{patientId}_{timestamp}.gdt"
            : _settings.FileNamePattern;

        var name = pattern
            .Replace("{sender}", _settings.SenderId, StringComparison.OrdinalIgnoreCase)
            .Replace("{receiver}", _settings.ReceiverId, StringComparison.OrdinalIgnoreCase)
            .Replace("{patientId}", report.Header.PatientId, StringComparison.OrdinalIgnoreCase)
            .Replace("{accession}", report.Header.AccessionNumber, StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        return Sanitize(name);
    }

    /// <summary>Entfernt alle in Dateinamen unzulaessigen Zeichen (Schutz gegen Path Traversal).</summary>
    public static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "export.gdt" : cleaned;
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name}_{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        throw new IOException($"Es konnte kein freier Dateiname fuer '{path}' gefunden werden.");
    }

    private static Encoding ResolveEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage <= 0 ? 28591 : codePage);
        }
        catch (NotSupportedException)
        {
            return Encoding.GetEncoding(28591); // ISO-8859-1 als sicherer Standard fuer GDT.
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(28591);
        }
    }
}
