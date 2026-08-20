namespace DCMtoGDTReports.Tools;

/// <summary>
/// Sucht eine DCMTK-Installation. DCMTK ist optional: das Programm arbeitet standardmaessig mit dem
/// eingebauten DICOM-Toolkit (fo-dicom) und nutzt DCMTK nur, wenn es konfiguriert bzw. auffindbar ist.
/// </summary>
public static class DcmtkLocator
{
    public const string Dsr2XmlExecutable = "dsr2xml.exe";
    public const string DcmDumpExecutable = "dcmdump.exe";

    /// <summary>Unterordner relativ zum Anwendungsverzeichnis, in den DCMTK lokal installiert wird.</summary>
    public const string BundledRelativePath = @"tools\dcmtk";

    private static readonly string[] WellKnownRoots =
    [
        @"C:\Program Files\DCMTK",
        @"C:\Program Files (x86)\DCMTK",
        @"C:\dcmtk",
        @"C:\tools\dcmtk"
    ];

    /// <summary>
    /// Sucht DCMTK in dieser Reihenfolge: konfigurierter Pfad, mitgelieferter tools-Ordner,
    /// PATH-Variable, bekannte Standardorte.
    /// </summary>
    /// <param name="configuredPath">Pfad aus der Konfiguration. Darf Ordner oder dsr2xml.exe direkt sein.</param>
    public static DcmtkInstallation? Locate(string? configuredPath = null, string? applicationDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fromConfig = FromCandidateDirectory(ResolveBinDirectory(configuredPath), DcmtkSource.Configured);
            if (fromConfig is not null) return fromConfig;
        }

        var appDir = applicationDirectory ?? AppContext.BaseDirectory;
        var bundled = FromCandidateDirectory(Path.Combine(appDir, BundledRelativePath, "bin"), DcmtkSource.Bundled)
                      ?? FromCandidateDirectory(Path.Combine(appDir, BundledRelativePath), DcmtkSource.Bundled);
        if (bundled is not null) return bundled;

        var fromPath = FromEnvironmentPath();
        if (fromPath is not null) return fromPath;

        foreach (var root in WellKnownRoots)
        {
            var found = FromCandidateDirectory(Path.Combine(root, "bin"), DcmtkSource.WellKnownLocation)
                        ?? FromCandidateDirectory(root, DcmtkSource.WellKnownLocation);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>Ziel-Installationsverzeichnis fuer einen lokalen DCMTK-Download.</summary>
    public static string GetBundledInstallDirectory(string? applicationDirectory = null)
        => Path.Combine(applicationDirectory ?? AppContext.BaseDirectory, BundledRelativePath);

    /// <summary>Klartext-Hinweis fuer die GUI, wo dsr2xml.exe erwartet wird.</summary>
    public static string BuildNotFoundHint(string? applicationDirectory = null)
    {
        var appDir = applicationDirectory ?? AppContext.BaseDirectory;
        var lines = new List<string>
        {
            "DCMTK wurde nicht gefunden. Es wird " + Dsr2XmlExecutable + " an einem dieser Orte erwartet:",
            "  1. Der in den Einstellungen hinterlegte DCMTK-Pfad",
            "  2. " + Path.Combine(appDir, BundledRelativePath, "bin"),
            "  3. Ein Verzeichnis aus der PATH-Umgebungsvariable"
        };
        lines.AddRange(WellKnownRoots.Select((r, i) => $"  {i + 4}. {Path.Combine(r, "bin")}"));
        lines.Add("Hinweis: DCMTK ist optional. Die Verarbeitung funktioniert auch mit dem eingebauten DICOM-Toolkit.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveBinDirectory(string configuredPath)
    {
        // Der Benutzer darf sowohl den Ordner als auch die exe selbst auswaehlen.
        if (File.Exists(configuredPath))
            return Path.GetDirectoryName(configuredPath) ?? configuredPath;

        var withBin = Path.Combine(configuredPath, "bin");
        return Directory.Exists(withBin) ? withBin : configuredPath;
    }

    private static DcmtkInstallation? FromCandidateDirectory(string directory, DcmtkSource source)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        var dsr2Xml = Path.Combine(directory, Dsr2XmlExecutable);
        if (!File.Exists(dsr2Xml))
            return null;

        var dcmDump = Path.Combine(directory, DcmDumpExecutable);
        return new DcmtkInstallation
        {
            BinPath = directory,
            Dsr2XmlPath = dsr2Xml,
            DcmDumpPath = File.Exists(dcmDump) ? dcmDump : null,
            Source = source
        };
    }

    private static DcmtkInstallation? FromEnvironmentPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable)) return null;

        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(entry.Trim('"'));
            }
            catch (ArgumentException)
            {
                continue; // Ungueltige PATH-Eintraege ignorieren statt die Suche abzubrechen.
            }

            var found = FromCandidateDirectory(candidate, DcmtkSource.EnvironmentPath);
            if (found is not null) return found;
        }

        return null;
    }
}
