using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DCMtoGDTReports.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DCMtoGDTReports.Core.Updating;

/// <summary>
/// Sucht, laedt und entpackt Programmaktualisierungen. Die Verteilung erfolgt ueber eine
/// zentrale update.json plus ZIP-Paket - entweder auf einem Netzlaufwerk (UNC) oder per HTTPS.
/// So bekommen alle Arbeitsplaetze denselben Stand, ohne dass jemand Dateien von Hand kopiert.
/// </summary>
public sealed class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly UpdateSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public UpdateService(UpdateSettings settings, HttpClient? httpClient = null, ILogger<UpdateService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _logger = logger ?? NullLogger<UpdateService>.Instance;
    }

    /// <summary>Version der laufenden Anwendung.</summary>
    public static Version InstalledVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? typeof(UpdateService).Assembly.GetName().Version
        ?? new Version(0, 0, 0, 0);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var installed = InstalledVersion;

        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.ManifestUrl))
            return new UpdateCheckResult(UpdateAvailability.NotConfigured, installed, null,
                "Die Updatepruefung ist nicht konfiguriert.");

        try
        {
            var manifest = await LoadManifestAsync(_settings.ManifestUrl, ct).ConfigureAwait(false);
            var available = manifest.ParsedVersion;

            if (available is null)
                return new UpdateCheckResult(UpdateAvailability.Failed, installed, null,
                    $"Die Versionsangabe '{manifest.Version}' im Manifest ist ungueltig.");

            // Nur die ersten drei Stellen vergleichen: Build-Nummern unterscheiden sich sonst unnoetig.
            return Normalize(available) > Normalize(installed)
                ? new UpdateCheckResult(UpdateAvailability.UpdateAvailable, installed, manifest,
                    $"Version {manifest.Version} steht bereit (installiert: {installed.ToString(3)}).")
                : new UpdateCheckResult(UpdateAvailability.UpToDate, installed, manifest,
                    $"Die installierte Version {installed.ToString(3)} ist aktuell.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Updatepruefung fehlgeschlagen.");
            return new UpdateCheckResult(UpdateAvailability.Failed, installed, null,
                $"Updatepruefung fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>Liest das Manifest von einer HTTPS-Adresse oder aus dem Dateisystem/UNC-Pfad.</summary>
    public async Task<UpdateManifest> LoadManifestAsync(string location, CancellationToken ct = default)
    {
        string json;
        if (IsHttp(location, out var uri))
        {
            EnsureHttps(uri!);
            json = await _httpClient.GetStringAsync(uri, ct).ConfigureAwait(false);
        }
        else
        {
            json = await File.ReadAllTextAsync(location, ct).ConfigureAwait(false);
        }

        return JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
               ?? throw new JsonException("Das Updatemanifest konnte nicht gelesen werden.");
    }

    /// <summary>
    /// Laedt das Paket, prueft die Pruefsumme und entpackt es in ein Staging-Verzeichnis.
    /// Es wird nichts an der laufenden Installation veraendert.
    /// </summary>
    public async Task<string> DownloadAndStageAsync(
        UpdateManifest manifest,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (string.IsNullOrWhiteSpace(manifest.Sha256))
            throw new InvalidOperationException("Das Manifest enthaelt keine SHA256-Pruefsumme. Installation abgebrochen.");

        var packageLocation = ResolvePackageLocation(_settings.ManifestUrl, manifest.PackageUrl);
        var tempZip = Path.Combine(Path.GetTempPath(), $"dcm2gdt-update-{Guid.NewGuid():N}.zip");

        try
        {
            progress?.Report($"Lade Paket {manifest.Version} ...");
            await FetchAsync(packageLocation, tempZip, ct).ConfigureAwait(false);

            progress?.Report("Pruefe Pruefsumme ...");
            var actual = await ComputeSha256Async(tempZip, ct).ConfigureAwait(false);
            var expected = manifest.Sha256.Replace("-", string.Empty).Trim();
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SHA256 stimmt nicht ueberein. Erwartet {expected}, berechnet {actual}.");

            var stagingDirectory = Path.Combine(Path.GetTempPath(), $"dcm2gdt-staging-{Guid.NewGuid():N}");
            progress?.Report("Entpacke Paket ...");
            ExtractSafely(tempZip, stagingDirectory);

            _logger.LogInformation("Update {Version} wurde nach {Staging} vorbereitet.", manifest.Version, stagingDirectory);
            return stagingDirectory;
        }
        finally
        {
            TryDelete(tempZip);
        }
    }

    /// <summary>
    /// Loest den Paketpfad auf. Relative Angaben beziehen sich auf den Ort des Manifests,
    /// damit ein Netzlaufwerk ohne absolute Pfade auskommt.
    /// </summary>
    public static string ResolvePackageLocation(string manifestLocation, string packageUrl)
    {
        if (string.IsNullOrWhiteSpace(packageUrl))
            throw new InvalidOperationException("Das Manifest enthaelt keinen Paketpfad.");

        if (IsHttp(packageUrl, out _) || Path.IsPathRooted(packageUrl))
            return packageUrl;

        if (IsHttp(manifestLocation, out var manifestUri))
            return new Uri(manifestUri!, packageUrl).ToString();

        var directory = Path.GetDirectoryName(manifestLocation);
        return string.IsNullOrEmpty(directory) ? packageUrl : Path.Combine(directory, packageUrl);
    }

    private async Task FetchAsync(string location, string targetPath, CancellationToken ct)
    {
        if (IsHttp(location, out var uri))
        {
            EnsureHttps(uri!);
            using var response = await _httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(targetPath);
            await source.CopyToAsync(target, ct).ConfigureAwait(false);
        }
        else
        {
            File.Copy(location, targetPath, overwrite: true);
        }
    }

    /// <summary>Entpackt mit Schutz gegen Zip-Slip.</summary>
    private static void ExtractSafely(string zipPath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var root = Path.GetFullPath(targetDirectory);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsicherer Archiveintrag wurde abgelehnt: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(0, version.Build));

    private static bool IsHttp(string location, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(location, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;

        uri = parsed;
        return true;
    }

    private static void EnsureHttps(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Updates duerfen nur ueber HTTPS oder ein Netzlaufwerk bezogen werden.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Temporaerdatei bleibt liegen - kein Grund das Update scheitern zu lassen.
        }
    }
}
