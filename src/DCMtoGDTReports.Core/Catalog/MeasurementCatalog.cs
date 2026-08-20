using System.Text.Json.Serialization;

namespace DCMtoGDTReports.Core.Catalog;

/// <summary>Art eines Katalogeintrags - bestimmt, worauf sich die Auswahl bezieht.</summary>
public enum CatalogEntryKind
{
    /// <summary>Messgroesse, z. B. "EF" oder "LVIDd".</summary>
    Measurement,

    /// <summary>Anatomische Region, z. B. "Lateral Mitral Annulus".</summary>
    Region,

    /// <summary>Aufnahmemodus, z. B. "Doppler Pulsed".</summary>
    ImageMode
}

/// <summary>
/// Ein aus SR-Dateien gelernter Eintrag. Ueber <see cref="Selected"/> wird in der GUI
/// festgelegt, ob er in die GDT-Datei uebernommen wird.
/// </summary>
public sealed class CatalogEntry
{
    /// <summary>Eindeutiger Schluessel: Concept-Code bzw. der Name der Region/des Modus.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Anzeigename in der Auswahlliste.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Kurzbezeichnung, sofern gemappt.</summary>
    public string ShortName { get; set; } = string.Empty;

    public CatalogEntryKind Kind { get; set; }

    /// <summary>Wird uebernommen. Neue Eintraege sind bewusst immer ausgewaehlt.</summary>
    public bool Selected { get; set; } = true;

    /// <summary>Einheit des zuletzt gesehenen Werts, z. B. "cm".</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Wie oft der Eintrag in ausgewerteten Dateien vorkam.</summary>
    public int SeenCount { get; set; }

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Anzeigetext fuer die Auswahlliste.</summary>
    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(ShortName) || ShortName == DisplayName
        ? DisplayName
        : $"{ShortName} - {DisplayName}";
}

/// <summary>
/// Was das Programm bisher in SR-Dateien gesehen hat. Der Katalog waechst mit jeder
/// ausgewerteten Datei und dient als Auswahlliste in der Oberflaeche.
/// </summary>
public sealed class MeasurementCatalog
{
    /// <summary>Auswahl anwenden. Ohne Haken werden alle gefundenen Werte uebernommen.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Messwerte uebernehmen, die noch nicht im Katalog stehen. Standard true, damit ein
    /// neues Geraet oder eine neue Messgroesse nicht unbemerkt verloren geht.
    /// </summary>
    public bool IncludeUnknown { get; set; } = true;

    public List<CatalogEntry> Measurements { get; set; } = [];
    public List<CatalogEntry> Regions { get; set; } = [];
    public List<CatalogEntry> ImageModes { get; set; } = [];

    public DateTime? LastLearnedUtc { get; set; }

    /// <summary>Anzahl der Dateien, aus denen gelernt wurde.</summary>
    public int LearnedFileCount { get; set; }

    [JsonIgnore]
    public IEnumerable<CatalogEntry> AllEntries => Measurements.Concat(Regions).Concat(ImageModes);

    [JsonIgnore]
    public bool IsEmpty => Measurements.Count == 0 && Regions.Count == 0 && ImageModes.Count == 0;

    public MeasurementCatalog Clone() => new()
    {
        Enabled = Enabled,
        IncludeUnknown = IncludeUnknown,
        LastLearnedUtc = LastLearnedUtc,
        LearnedFileCount = LearnedFileCount,
        Measurements = Measurements.Select(Copy).ToList(),
        Regions = Regions.Select(Copy).ToList(),
        ImageModes = ImageModes.Select(Copy).ToList()
    };

    private static CatalogEntry Copy(CatalogEntry e) => new()
    {
        Key = e.Key,
        DisplayName = e.DisplayName,
        ShortName = e.ShortName,
        Kind = e.Kind,
        Selected = e.Selected,
        Unit = e.Unit,
        SeenCount = e.SeenCount,
        LastSeenUtc = e.LastSeenUtc
    };
}
