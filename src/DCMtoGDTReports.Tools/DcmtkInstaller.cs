using System.IO.Compression;
using System.Security.Cryptography;

namespace DCMtoGDTReports.Tools;

/// <summary>
/// Optionale Installation von DCMTK in den lokalen tools-Ordner.
/// Wichtig: Es wird niemals automatisch heruntergeladen - der Aufruf muss immer
/// durch eine explizite Benutzeraktion ausgeloest werden.
/// </summary>
public sealed class DcmtkInstaller(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Offizielles DCMTK-Windows-Paket von OFFIS. Bewusst als Konstante hinterlegt und in der
    /// Konfiguration ueberschreibbar, damit im Firmennetz ein interner Mirror genutzt werden kann.
    /// </summary>
    public const string DefaultDownloadUrl = "https://dicom.offis.de/download/dcmtk/dcmtk368/bin/dcmtk-3.6.8-win64-dynamic.zip";

    /// <summary>
    /// Laedt ein DCMTK-ZIP herunter und entpackt es in das Zielverzeichnis.
    /// </summary>
    /// <param name="downloadUrl">Nur HTTPS. Standard ist das offizielle OFFIS-Paket.</param>
    /// <param name="targetDirectory">Zielordner, ueblicherweise &lt;App&gt;\tools\dcmtk.</param>
    /// <param name="expectedSha256">Optionaler Hex-Hash zur Integritaetspruefung.</param>
    public async Task<DcmtkInstallation> DownloadAndInstallAsync(
        string downloadUrl,
        string targetDirectory,
        string? expectedSha256 = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Der Download ist nur ueber HTTPS erlaubt.", nameof(downloadUrl));

        var tempZip = Path.Combine(Path.GetTempPath(), $"dcmtk-{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report($"Lade DCMTK von {uri.Host} ...");
            using (var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(tempZip);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                progress?.Report("Pruefe Pruefsumme ...");
                var actual = await ComputeSha256Async(tempZip, ct).ConfigureAwait(false);
                if (!string.Equals(actual, expectedSha256.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"SHA256 stimmt nicht ueberein. Erwartet {expectedSha256}, berechnet {actual}.");
            }

            progress?.Report($"Entpacke nach {targetDirectory} ...");
            return InstallFromZip(tempZip, targetDirectory);
        }
        finally
        {
            TryDelete(tempZip);
        }
    }

    /// <summary>
    /// Installiert DCMTK aus einem bereits vorliegenden ZIP-Archiv (z. B. manuell heruntergeladen).
    /// </summary>
    public DcmtkInstallation InstallFromZip(string zipPath, string targetDirectory)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("ZIP-Archiv nicht gefunden.", zipPath);

        Directory.CreateDirectory(targetDirectory);
        var targetRoot = Path.GetFullPath(targetDirectory);

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // reiner Verzeichniseintrag

                // Die offiziellen Archive haben einen Wurzelordner (dcmtk-x.y.z-...) - den strippen wir weg.
                var relative = StripRootFolder(entry.FullName);
                if (string.IsNullOrEmpty(relative))
                    continue;

                var destination = Path.GetFullPath(Path.Combine(targetRoot, relative));

                // Schutz gegen Zip-Slip: Ziel muss innerhalb des Zielordners liegen.
                if (!destination.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Unsicherer Archiveintrag wurde abgelehnt: {entry.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        var installation = DcmtkLocator.Locate(targetDirectory)
            ?? throw new InvalidOperationException(
                $"Nach dem Entpacken wurde {DcmtkLocator.Dsr2XmlExecutable} in '{targetDirectory}' nicht gefunden.");

        return installation;
    }

    private static string StripRootFolder(string entryPath)
    {
        var normalized = entryPath.Replace('/', Path.DirectorySeparatorChar);
        var separatorIndex = normalized.IndexOf(Path.DirectorySeparatorChar);
        return separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : normalized;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Temporaerdatei bleibt liegen - kein Grund die Installation scheitern zu lassen.
        }
    }
}
