using System.Text.Json;
using System.Text.Json.Serialization;

namespace DCMtoGDTReports.Core.Configuration;

/// <summary>
/// Laedt und speichert die Konfiguration als settings.json. Der Speicherort ist konfigurierbar,
/// Standard ist ProgramData damit Dienst und GUI dieselbe Datei nutzen.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Enum-Werte als Klartext, damit settings.json von Hand lesbar und pflegbar bleibt.
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsFilePath { get; }

    public SettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? GetDefaultSettingsPath();
    }

    public static string GetDefaultSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "brans IT solutions",
        "DCMtoGDTReports",
        "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            var created = CreateDefaults();
            Save(created);
            return created;
        }

        var json = File.ReadAllText(SettingsFilePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefaults();
        ApplyFallbacks(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);

        // Atomar schreiben, damit ein parallel laufender Dienst nie eine halbe Datei liest.
        var tempPath = SettingsFilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, SettingsFilePath, overwrite: true);
    }

    /// <summary>Erzeugt die Standardkonfiguration mit Ordnern unterhalb von ProgramData.</summary>
    public AppSettings CreateDefaults()
    {
        var root = Path.GetDirectoryName(SettingsFilePath)!;
        var settings = new AppSettings
        {
            InputFolder = Path.Combine(root, "in"),
            OutputFolder = Path.Combine(root, "out"),
            ArchiveFolder = Path.Combine(root, "archive"),
            ErrorFolder = Path.Combine(root, "error"),
            LogFolder = Path.Combine(root, "logs"),
            RegistryDatabasePath = Path.Combine(root, "processed.db"),
            MeasurementCatalogPath = Path.Combine(root, "catalog.json")
        };
        return settings;
    }

    /// <summary>Fuellt leere Pfade mit sinnvollen Standardwerten auf.</summary>
    public void ApplyFallbacks(AppSettings settings)
    {
        var root = Path.GetDirectoryName(SettingsFilePath)!;
        if (string.IsNullOrWhiteSpace(settings.LogFolder))
            settings.LogFolder = Path.Combine(root, "logs");
        if (string.IsNullOrWhiteSpace(settings.RegistryDatabasePath))
            settings.RegistryDatabasePath = Path.Combine(root, "processed.db");
        if (string.IsNullOrWhiteSpace(settings.MeasurementCatalogPath))
            settings.MeasurementCatalogPath = Path.Combine(root, "catalog.json");
    }

    /// <summary>Legt alle konfigurierten Arbeitsordner an, soweit gesetzt.</summary>
    public static void EnsureFolders(AppSettings settings)
    {
        foreach (var folder in new[] { settings.InputFolder, settings.OutputFolder, settings.ArchiveFolder, settings.ErrorFolder, settings.LogFolder })
        {
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);
        }
    }

    /// <summary>Prueft die Konfiguration und liefert Klartextmeldungen fuer die GUI.</summary>
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.InputFolder)) problems.Add("Eingangsordner ist nicht gesetzt.");
        if (string.IsNullOrWhiteSpace(settings.OutputFolder)) problems.Add("Ausgabeordner ist nicht gesetzt.");
        if (string.IsNullOrWhiteSpace(settings.Gdt.SenderId)) problems.Add("GDT Senderkennung (8316) fehlt.");
        if (string.IsNullOrWhiteSpace(settings.Gdt.ReceiverId)) problems.Add("GDT Empfaengerkennung (8315) fehlt.");
        if (settings.Gdt.MaxResultLineLength is < 10 or > 200) problems.Add("MaxResultLineLength muss zwischen 10 und 200 liegen.");
        return problems;
    }
}
