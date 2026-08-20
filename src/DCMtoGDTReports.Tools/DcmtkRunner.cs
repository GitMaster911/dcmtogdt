using System.Diagnostics;
using System.Text;

namespace DCMtoGDTReports.Tools;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Kapselt die Aufrufe der DCMTK-Kommandozeilenprogramme.
/// </summary>
public sealed class DcmtkRunner(DcmtkInstallation installation)
{
    private readonly DcmtkInstallation _installation = installation
        ?? throw new ArgumentNullException(nameof(installation));

    public DcmtkInstallation Installation => _installation;

    /// <summary>
    /// Konvertiert eine DICOM-SR-Datei nach XML. Liefert den Pfad der erzeugten XML-Datei.
    /// </summary>
    public async Task<string> ConvertSrToXmlAsync(string dicomFilePath, string xmlOutputPath, CancellationToken ct = default)
    {
        if (!File.Exists(dicomFilePath))
            throw new FileNotFoundException("DICOM-Datei nicht gefunden.", dicomFilePath);

        Directory.CreateDirectory(Path.GetDirectoryName(xmlOutputPath)!);

        // +Ea = alle Attribute schreiben, +Ee = Code-Sequenzen als Elemente, --charset-assume latin-1 fuer SR ohne CharacterSet
        var result = await RunAsync(
            _installation.Dsr2XmlPath,
            ["+Ea", "+Ee", "--charset-assume", "latin-1", dicomFilePath, xmlOutputPath],
            ct).ConfigureAwait(false);

        if (!result.Success || !File.Exists(xmlOutputPath))
        {
            throw new InvalidOperationException(
                $"dsr2xml ist fehlgeschlagen (ExitCode {result.ExitCode}). {result.StandardError}".Trim());
        }

        return xmlOutputPath;
    }

    /// <summary>
    /// Fuehrt dcmdump aus. Nur fuer Debugging und Metadatenpruefung gedacht.
    /// </summary>
    public async Task<string> DumpAsync(string dicomFilePath, CancellationToken ct = default)
    {
        if (_installation.DcmDumpPath is null)
            throw new InvalidOperationException("dcmdump.exe ist in dieser DCMTK-Installation nicht vorhanden.");

        var result = await RunAsync(_installation.DcmDumpPath, ["--print-short", dicomFilePath], ct).ConfigureAwait(false);
        return result.Success ? result.StandardOutput : result.StandardOutput + Environment.NewLine + result.StandardError;
    }

    private static async Task<ProcessRunResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            throw new InvalidOperationException($"Prozess konnte nicht gestartet werden: {executable}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
