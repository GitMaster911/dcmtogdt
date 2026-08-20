using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DCMtoGDTReports.Core.Updating;

/// <summary>
/// Ersetzt die installierten Programmdateien durch ein vorbereitetes Update.
/// Eine laufende .exe kann sich unter Windows nicht selbst ueberschreiben. Deshalb wird ein
/// kleines Batch-Skript erzeugt, das auf das Ende des Programms wartet, die Dateien kopiert,
/// den Dienst neu startet und sich anschliessend selbst loescht.
/// </summary>
public sealed class UpdateInstaller(ILogger<UpdateInstaller>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<UpdateInstaller>.Instance;

    /// <summary>
    /// Startet die Installation und liefert true, wenn das Skript laeuft. Der Aufrufer muss
    /// die Anwendung danach umgehend beenden, damit die Dateien freigegeben werden.
    /// </summary>
    /// <param name="stagingDirectory">Entpacktes Update (Ergebnis von <see cref="UpdateService.DownloadAndStageAsync"/>).</param>
    /// <param name="targetDirectory">Installationsverzeichnis, standardmaessig das Anwendungsverzeichnis.</param>
    /// <param name="restartExecutable">Programm, das nach dem Update wieder gestartet wird. Null = kein Neustart.</param>
    /// <param name="serviceName">Optionaler Windows-Dienst, der gestoppt und wieder gestartet wird.</param>
    public bool Install(
        string stagingDirectory,
        string? targetDirectory = null,
        string? restartExecutable = null,
        string? serviceName = null)
    {
        if (!Directory.Exists(stagingDirectory))
            throw new DirectoryNotFoundException($"Das vorbereitete Update wurde nicht gefunden: {stagingDirectory}");

        var target = targetDirectory ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var logPath = Path.Combine(Path.GetTempPath(), "dcm2gdt-update.log");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"dcm2gdt-update-{Guid.NewGuid():N}.cmd");

        File.WriteAllText(
            scriptPath,
            BuildScript(Environment.ProcessId, stagingDirectory, target, restartExecutable, serviceName, logPath),
            Encoding.ASCII);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };

        var process = Process.Start(startInfo);
        if (process is null)
        {
            _logger.LogError("Das Updateskript konnte nicht gestartet werden.");
            return false;
        }

        _logger.LogInformation("Update wird installiert. Protokoll: {LogPath}", logPath);
        return true;
    }

    /// <summary>
    /// Bewusst als ASCII-Batch erzeugt: damit gibt es keine Probleme mit der Zeichenkodierung
    /// und keine Abhaengigkeit von der PowerShell-Ausfuehrungsrichtlinie.
    /// </summary>
    private static string BuildScript(
        int processId,
        string stagingDirectory,
        string targetDirectory,
        string? restartExecutable,
        string? serviceName,
        string logPath)
    {
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("setlocal");
        script.AppendLine($"echo [%DATE% %TIME%] Update gestartet >> \"{logPath}\"");

        // Der Dienst wird zuerst gestoppt. Loest das Update der Dienst selbst aus, endet damit
        // gleichzeitig der wartende Prozess - die Wartschleife greift dann fuer beide Faelle.
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            script.AppendLine($"net stop \"{serviceName}\" >> \"{logPath}\" 2>&1");
            script.AppendLine("ping -n 3 127.0.0.1 >nul");
        }

        // Auf das Ende des aufrufenden Prozesses warten (max. 120 Sekunden).
        script.AppendLine("set /a WAITED=0");
        script.AppendLine(":waitloop");
        script.AppendLine($"tasklist /FI \"PID eq {processId}\" /NH | find \"{processId}\" >nul");
        script.AppendLine("if errorlevel 1 goto stopped");
        script.AppendLine("ping -n 2 127.0.0.1 >nul");
        script.AppendLine("set /a WAITED+=1");
        script.AppendLine("if %WAITED% LSS 120 goto waitloop");
        script.AppendLine($"echo [%DATE% %TIME%] Zeitueberschreitung beim Warten auf PID {processId} >> \"{logPath}\"");
        script.AppendLine(":stopped");

        script.AppendLine($"robocopy \"{stagingDirectory}\" \"{targetDirectory}\" /E /R:5 /W:2 /NFL /NDL /NJH /NJS >> \"{logPath}\" 2>&1");
        // Robocopy meldet ab 8 einen echten Fehler; darunter sind Erfolgsmeldungen.
        script.AppendLine($"if %ERRORLEVEL% GEQ 8 echo [%DATE% %TIME%] FEHLER beim Kopieren, ErrorLevel %ERRORLEVEL% >> \"{logPath}\"");

        if (!string.IsNullOrWhiteSpace(serviceName))
            script.AppendLine($"net start \"{serviceName}\" >> \"{logPath}\" 2>&1");

        if (!string.IsNullOrWhiteSpace(restartExecutable))
            script.AppendLine($"start \"\" \"{restartExecutable}\"");

        script.AppendLine($"rmdir /s /q \"{stagingDirectory}\"");
        script.AppendLine($"echo [%DATE% %TIME%] Update beendet >> \"{logPath}\"");
        script.AppendLine("del \"%~f0\"");

        return script.ToString();
    }
}
