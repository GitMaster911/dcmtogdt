using System.Text.Json.Serialization;

namespace DCMtoGDTReports.Core.Updating;

/// <summary>
/// Inhalt der Datei update.json, die zentral (Netzlaufwerk oder HTTPS) bereitgestellt wird.
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>Versionsnummer des bereitgestellten Pakets, z. B. "1.2.0".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Ablageort des ZIP-Pakets. Relative Angaben werden auf den Ort des Manifests bezogen,
    /// damit ein Netzlaufwerk ohne absolute Pfade auskommt.
    /// </summary>
    public string PackageUrl { get; set; } = string.Empty;

    /// <summary>SHA256 des Pakets in Hex. Pflicht - ohne Pruefsumme wird nicht installiert.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Kurze Beschreibung der Aenderungen fuer die Anzeige in der GUI.</summary>
    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Pflichtupdate: die GUI weist deutlicher darauf hin.</summary>
    public bool Mandatory { get; set; }

    [JsonIgnore]
    public Version? ParsedVersion =>
        System.Version.TryParse(Version, out var parsed) ? parsed : null;
}

public enum UpdateAvailability
{
    /// <summary>Die installierte Version ist aktuell.</summary>
    UpToDate,

    /// <summary>Ein neueres Paket steht bereit.</summary>
    UpdateAvailable,

    /// <summary>Updatepruefung ist nicht konfiguriert oder deaktiviert.</summary>
    NotConfigured,

    /// <summary>Die Pruefung ist fehlgeschlagen.</summary>
    Failed
}

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    Version InstalledVersion,
    UpdateManifest? Manifest,
    string Message)
{
    public bool IsUpdateAvailable => Availability == UpdateAvailability.UpdateAvailable && Manifest is not null;
}
