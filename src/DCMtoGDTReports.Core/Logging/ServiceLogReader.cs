using System.Text;

namespace DCMtoGDTReports.Core.Logging;

/// <summary>
/// Liest die Logdatei des Windows-Dienstes fortlaufend mit, damit die Oberflaeche auch das
/// zeigt, was im Hintergrund passiert. Ohne das bliebe das Protokollfenster leer, sobald die
/// Verarbeitung nicht in der Oberflaeche, sondern im Dienst laeuft.
/// </summary>
public sealed class ServiceLogReader(string logFolder, string filePattern = "worker-*.log")
{
    private string? _currentFile;
    private long _position;

    /// <summary>
    /// Liefert die seit dem letzten Aufruf hinzugekommenen Zeilen. Beim ersten Aufruf werden
    /// hoechstens <paramref name="initialLines"/> Zeilen zurueckgegeben, damit die Oberflaeche
    /// nicht mit dem gesamten Tagesprotokoll geflutet wird.
    /// </summary>
    public IReadOnlyList<string> ReadNewLines(int initialLines = 100)
    {
        var file = FindNewestLogFile();
        if (file is null) return [];

        // Tageswechsel oder abgeschnittene Datei: von vorne beginnen.
        if (!string.Equals(file, _currentFile, StringComparison.OrdinalIgnoreCase))
        {
            _currentFile = file;
            _position = 0;
        }

        try
        {
            // FileShare.ReadWrite ist zwingend - Serilog haelt die Datei im Dienst offen.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (_position > stream.Length) _position = 0;

            var isFirstRead = _position == 0;
            stream.Seek(_position, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }

            _position = stream.Length;
            return isFirstRead && lines.Count > initialLines
                ? lines[^initialLines..]
                : lines;
        }
        catch (IOException)
        {
            // Datei gerade gesperrt - beim naechsten Durchlauf erneut versuchen.
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string? FindNewestLogFile()
    {
        if (string.IsNullOrWhiteSpace(logFolder) || !Directory.Exists(logFolder)) return null;

        try
        {
            return new DirectoryInfo(logFolder)
                .EnumerateFiles(filePattern)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
