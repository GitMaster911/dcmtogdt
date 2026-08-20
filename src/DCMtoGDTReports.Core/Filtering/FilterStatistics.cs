namespace DCMtoGDTReports.Core.Filtering;

/// <summary>Stufe, an der ein Messwert aussortiert wurde.</summary>
public enum FilterReason
{
    CatalogMeasurement,
    CatalogRegion,
    CatalogImageMode,
    OnlySelected,
    OnlyMapped,
    ConceptPattern,
    SitePattern,
    ModePattern
}

/// <summary>
/// Aufschluesselung eines Filterlaufs. Steht am Ende nur eine Handvoll Werte im Befund, laesst
/// sich damit im Protokoll sofort ablesen, welche Einstellung dafuer verantwortlich ist.
/// </summary>
public sealed class FilterStatistics
{
    private readonly Dictionary<FilterReason, int> _removed = [];

    public int Input { get; set; }
    public int Output { get; set; }

    /// <summary>Durch Zusammenfassen von Wiederholungsmessungen entfallen (Min/Max/Mittel).</summary>
    public int CondensedAway { get; set; }

    /// <summary>Durch die Obergrenze MaxMeasurements abgeschnitten.</summary>
    public int RemovedByLimit { get; set; }

    public void Count(FilterReason reason)
        => _removed[reason] = _removed.GetValueOrDefault(reason) + 1;

    public int RemovedBy(FilterReason reason) => _removed.GetValueOrDefault(reason);

    public bool RemovedAnything => _removed.Count > 0 || CondensedAway > 0 || RemovedByLimit > 0;

    /// <summary>Klartext fuer das Protokoll, absteigend nach Anzahl. Leer, wenn nichts entfiel.</summary>
    public string Describe()
    {
        var parts = _removed
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .Select(p => $"{Describe(p.Key)}: {p.Value}")
            .ToList();

        if (CondensedAway > 0) parts.Add($"zusammengefasst: {CondensedAway}");
        if (RemovedByLimit > 0) parts.Add($"Obergrenze: {RemovedByLimit}");

        return string.Join(", ", parts);
    }

    private static string Describe(FilterReason reason) => reason switch
    {
        FilterReason.CatalogMeasurement => "Messgroesse im Katalog abgewaehlt",
        FilterReason.CatalogRegion => "Region im Katalog abgewaehlt",
        FilterReason.CatalogImageMode => "Aufnahmemodus im Katalog abgewaehlt",
        FilterReason.OnlySelected => "ohne Selection Status",
        FilterReason.OnlyMapped => "ohne hinterlegten Kurznamen",
        FilterReason.ConceptPattern => "Muster Messgroesse",
        FilterReason.SitePattern => "Muster Region",
        FilterReason.ModePattern => "Muster Aufnahmemodus",
        _ => reason.ToString()
    };
}
