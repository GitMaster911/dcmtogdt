using Microsoft.Extensions.Logging;

namespace DCMtoGDTReports.Core.Processing;

/// <summary>
/// Verschiebt verarbeitete DICOM-Dateien in den Importordner des PVS.
///
/// Geschrieben wird immer zweistufig: erst eine .tmp-Datei im Zielordner, danach die
/// Umbenennung auf den endgueltigen Namen. Das Umbenennen innerhalb eines Ordners ist
/// atomar - das PVS sieht die Datei also erst, wenn sie vollstaendig da ist.
/// </summary>
public sealed class DicomForwarder(string targetFolder, ILogger logger)
{
    private const string TempExtension = ".tmp";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(targetFolder);

    /// <summary>
    /// Verschiebt die Datei in den Zielordner. Liefert true, wenn sie dort angekommen ist.
    /// Die Quelldatei wird erst geloescht, wenn das Ziel vollstaendig geschrieben wurde.
    /// </summary>
    public bool Forward(string sourceFilePath)
    {
        if (!IsConfigured || !File.Exists(sourceFilePath)) return false;

        var tempPath = string.Empty;
        try
        {
            Directory.CreateDirectory(targetFolder);

            var finalPath = MakeUnique(Path.Combine(targetFolder, Path.GetFileName(sourceFilePath)));
            tempPath = finalPath + TempExtension;

            File.Copy(sourceFilePath, tempPath, overwrite: true);
            File.Move(tempPath, finalPath);
            tempPath = string.Empty;

            File.Delete(sourceFilePath);

            logger.LogInformation("{File} an das PVS weitergereicht.", Path.GetFileName(finalPath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Datei bleibt im Eingang liegen und wird beim naechsten Durchlauf erneut versucht.
            logger.LogError(ex, "Weiterleitung von {File} fehlgeschlagen.", Path.GetFileName(sourceFilePath));
            CleanUpTemp(tempPath);
            return false;
        }
    }

    private void CleanUpTemp(string tempPath)
    {
        if (string.IsNullOrEmpty(tempPath)) return;

        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Unvollstaendige Zieldatei konnte nicht entfernt werden.");
        }
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
}
