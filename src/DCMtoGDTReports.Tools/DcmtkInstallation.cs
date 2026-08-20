namespace DCMtoGDTReports.Tools;

/// <summary>
/// Beschreibt eine gefundene DCMTK-Installation inklusive der beiden fuer uns relevanten Programme.
/// </summary>
public sealed class DcmtkInstallation
{
    public required string BinPath { get; init; }

    /// <summary>Vollstaendiger Pfad zu dsr2xml.exe (Pflicht fuer die SR-Konvertierung).</summary>
    public required string Dsr2XmlPath { get; init; }

    /// <summary>Vollstaendiger Pfad zu dcmdump.exe (optional, nur fuer Debugging/Metadaten).</summary>
    public string? DcmDumpPath { get; init; }

    /// <summary>Woher die Installation stammt - wird in der GUI angezeigt.</summary>
    public required DcmtkSource Source { get; init; }

    public bool HasDcmDump => !string.IsNullOrEmpty(DcmDumpPath) && File.Exists(DcmDumpPath);

    public override string ToString() => $"{BinPath} ({Source})";
}

public enum DcmtkSource
{
    /// <summary>Pfad kommt aus der Konfigurationsdatei.</summary>
    Configured,

    /// <summary>Mitgeliefert bzw. lokal nach tools\dcmtk installiert.</summary>
    Bundled,

    /// <summary>Ueber die PATH-Variable gefunden.</summary>
    EnvironmentPath,

    /// <summary>An einem bekannten Standard-Installationsort gefunden.</summary>
    WellKnownLocation
}
