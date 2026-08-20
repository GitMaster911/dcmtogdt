using System.Globalization;
using System.Text.RegularExpressions;
using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Mapping;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Filtering;

/// <summary>
/// Reduziert die Messwerte eines Structured Reports auf die klinisch gewuenschte Auswahl.
/// Der GE Vivid T8 liefert pro Untersuchung mehrere hundert Werte (u. a. 18 Strain-Segmente und
/// je Herzschlag einen eigenen EF-Wert), was fuer einen Krankenblatteintrag meist zu viel ist.
///
/// Zwei Wege fuehren zur Auswahl: die Ankreuzliste des gelernten Katalogs und - fuer
/// Fortgeschrittene - Textmuster. Beide lassen sich kombinieren.
/// </summary>
public sealed class MeasurementFilter(
    MeasurementFilterSettings settings,
    GdtSettings gdtSettings,
    MeasurementMapper mapper,
    MeasurementCatalog? catalog = null)
{
    private readonly MeasurementFilterSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly GdtSettings _gdt = gdtSettings ?? throw new ArgumentNullException(nameof(gdtSettings));
    private readonly MeasurementMapper _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    private readonly MeasurementCatalog? _catalog = catalog;

    public IReadOnlyList<MeasurementResult> Apply(IReadOnlyList<MeasurementResult> measurements)
        => Apply(measurements, out _);

    /// <summary>
    /// Wie <see cref="Apply(IReadOnlyList{MeasurementResult})"/>, liefert zusaetzlich die
    /// Aufschluesselung, welche Filterstufe wie viele Messwerte entfernt hat. Ohne diese Angabe
    /// ist im Betrieb nicht nachvollziehbar, warum am Ende nur wenige Werte im Befund stehen.
    /// </summary>
    public IReadOnlyList<MeasurementResult> Apply(
        IReadOnlyList<MeasurementResult> measurements, out FilterStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(measurements);

        statistics = new FilterStatistics { Input = measurements.Count };

        var catalogActive = _catalog is { Enabled: true } && !_catalog.IsEmpty;
        if (!_settings.Enabled && !catalogActive)
        {
            statistics.Output = measurements.Count;
            return measurements;
        }

        var includeConcepts = Compile(_settings.IncludeConcepts);
        var excludeConcepts = Compile(_settings.ExcludeConcepts);
        var includeSites = Compile(_settings.IncludeFindingSites);
        var excludeSites = Compile(_settings.ExcludeFindingSites);
        var includeModes = Compile(_settings.IncludeImageModes);
        var excludeModes = Compile(_settings.ExcludeImageModes);

        var selected = new List<MeasurementResult>();
        foreach (var m in measurements)
        {
            var reason = FindRejectionReason(m, catalogActive,
                includeConcepts, excludeConcepts, includeSites, excludeSites, includeModes, excludeModes);

            if (reason is null) selected.Add(m);
            else statistics.Count(reason.Value);
        }

        var result = CondenseRepeatedValues(selected);
        statistics.CondensedAway = selected.Count - result.Count;

        if (_settings.MaxMeasurements > 0 && result.Count > _settings.MaxMeasurements)
        {
            statistics.RemovedByLimit = result.Count - _settings.MaxMeasurements;
            result = result.Take(_settings.MaxMeasurements).ToList();
        }

        statistics.Output = result.Count;
        return result;
    }

    /// <summary>Liefert die erste Stufe, an der ein Messwert scheitert - oder null, wenn er bleibt.</summary>
    private FilterReason? FindRejectionReason(
        MeasurementResult m, bool catalogActive,
        IReadOnlyList<Regex> includeConcepts, IReadOnlyList<Regex> excludeConcepts,
        IReadOnlyList<Regex> includeSites, IReadOnlyList<Regex> excludeSites,
        IReadOnlyList<Regex> includeModes, IReadOnlyList<Regex> excludeModes)
    {
        if (catalogActive)
        {
            var catalogReason = FindCatalogRejection(m);
            if (catalogReason is not null) return catalogReason;
        }

        if (!_settings.Enabled) return null;

        if (_settings.OnlySelectedValues && string.IsNullOrEmpty(m.SelectionStatus)) return FilterReason.OnlySelected;
        if (_settings.OnlyMappedMeasurements && !_mapper.HasMapping(m)) return FilterReason.OnlyMapped;
        if (!Passes(includeConcepts, excludeConcepts, m.SourceCode, m.Name, m.ShortName)) return FilterReason.ConceptPattern;
        if (!Passes(includeSites, excludeSites, m.FindingSite)) return FilterReason.SitePattern;
        if (!Passes(includeModes, excludeModes, m.ImageMode)) return FilterReason.ModePattern;

        return null;
    }

    private FilterReason? FindCatalogRejection(MeasurementResult measurement)
    {
        var catalog = _catalog!;

        if (!IsAllowed(catalog.Measurements, MeasurementCatalogService.MeasurementKey(measurement)))
            return FilterReason.CatalogMeasurement;
        if (!IsAllowed(catalog.Regions, measurement.FindingSite))
            return FilterReason.CatalogRegion;
        if (!IsAllowed(catalog.ImageModes, measurement.ImageMode))
            return FilterReason.CatalogImageMode;

        return null;

        bool IsAllowed(List<CatalogEntry> entries, string key)
        {
            if (string.IsNullOrWhiteSpace(key) || entries.Count == 0) return true;

            var entry = entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            return entry is null ? catalog.IncludeUnknown : entry.Selected;
        }
    }

    /// <summary>
    /// Prueft die Ankreuzauswahl des Katalogs. Eintraege, die noch nicht gelernt wurden,
    /// werden je nach Einstellung uebernommen - damit gehen neue Messgroessen nicht verloren.
    /// </summary>
    private bool PassesCatalog(MeasurementResult measurement) => FindCatalogRejection(measurement) is null;

    /// <summary>
    /// Ausschluss hat Vorrang. Eine leere Einschlussliste bedeutet "alles zulassen".
    /// </summary>
    private static bool Passes(IReadOnlyList<Regex> include, IReadOnlyList<Regex> exclude, params string[] values)
    {
        if (exclude.Count > 0 && Matches(exclude, values)) return false;
        return include.Count == 0 || Matches(include, values);
    }

    private static bool Matches(IReadOnlyList<Regex> patterns, IReadOnlyList<string> values)
        => patterns.Any(p => values.Any(v => !string.IsNullOrEmpty(v) && p.IsMatch(v)));

    /// <summary>
    /// Fasst Wiederholungsmessungen derselben Messgroesse zusammen (z. B. EF je Herzschlag).
    /// Die Reihenfolge der ersten Vorkommen bleibt erhalten.
    /// </summary>
    private List<MeasurementResult> CondenseRepeatedValues(List<MeasurementResult> source)
    {
        if (_settings.RepeatedValues == RepeatedValueMode.All) return source;

        var groups = new List<List<MeasurementResult>>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var measurement in source)
        {
            var key = BuildGroupKey(measurement);
            if (indexByKey.TryGetValue(key, out var index))
            {
                groups[index].Add(measurement);
            }
            else
            {
                indexByKey[key] = groups.Count;
                groups.Add([measurement]);
            }
        }

        return groups.Select(Condense).ToList();
    }

    private MeasurementResult Condense(List<MeasurementResult> group)
    {
        if (group.Count == 1) return group[0];

        switch (_settings.RepeatedValues)
        {
            case RepeatedValueMode.First:
                return group[0];
            case RepeatedValueMode.Last:
                return group[^1];
        }

        var numeric = group.Where(m => m.NumericValue.HasValue).ToList();
        if (numeric.Count == 0) return group[0]; // Nicht rechenbar - ersten Wert behalten.

        var mean = numeric.Average(m => m.NumericValue!.Value);
        var min = numeric.Min(m => m.NumericValue!.Value);
        var max = numeric.Max(m => m.NumericValue!.Value);

        var (value, note) = _settings.RepeatedValues switch
        {
            RepeatedValueMode.Mean => (mean, $"Mittel aus {numeric.Count}"),
            RepeatedValueMode.Min => (min, $"Minimum aus {numeric.Count}"),
            RepeatedValueMode.Max => (max, $"Maximum aus {numeric.Count}"),
            RepeatedValueMode.MinMaxMean => (mean, $"Min {Format(min)} / Max {Format(max)}, n={numeric.Count}"),
            _ => (numeric[0].NumericValue!.Value, string.Empty)
        };

        var condensed = group[0].Clone();
        condensed.NumericValue = value;
        condensed.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
        condensed.Value = Format(value);
        condensed.AggregationNote = note;
        return condensed;
    }

    private string Format(double value) => DicomValueConverter.FormatNumeric(
        value.ToString("R", CultureInfo.InvariantCulture), _gdt.DecimalPlaces, _gdt.DecimalSeparator);

    /// <summary>Alles ausser dem eigentlichen Zahlenwert bildet die Messgroesse.</summary>
    private static string BuildGroupKey(MeasurementResult m) => string.Join('|',
        m.SourceCode, m.Name, m.FindingSite, m.Method, m.CardiacCyclePoint, m.DirectionOfFlow, m.ImageMode, m.Unit);

    /// <summary>Uebersetzt Muster mit * und ? in Regex-Ausdruecke.</summary>
    private static IReadOnlyList<Regex> Compile(IEnumerable<string>? patterns)
    {
        if (patterns is null) return [];

        return patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex(
                "^" + Regex.Escape(p.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .ToList();
    }
}
