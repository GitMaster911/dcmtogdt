using System.Globalization;
using System.Text.RegularExpressions;
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
/// Ist der Filter deaktiviert, wird die Liste unveraendert durchgereicht - es gehen also
/// niemals unbemerkt Werte verloren.
/// </summary>
public sealed class MeasurementFilter(
    MeasurementFilterSettings settings,
    GdtSettings gdtSettings,
    MeasurementMapper mapper)
{
    private readonly MeasurementFilterSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly GdtSettings _gdt = gdtSettings ?? throw new ArgumentNullException(nameof(gdtSettings));
    private readonly MeasurementMapper _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

    public IReadOnlyList<MeasurementResult> Apply(IReadOnlyList<MeasurementResult> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        if (!_settings.Enabled) return measurements;

        var includeConcepts = Compile(_settings.IncludeConcepts);
        var excludeConcepts = Compile(_settings.ExcludeConcepts);
        var includeSites = Compile(_settings.IncludeFindingSites);
        var excludeSites = Compile(_settings.ExcludeFindingSites);
        var includeModes = Compile(_settings.IncludeImageModes);
        var excludeModes = Compile(_settings.ExcludeImageModes);

        var selected = measurements.Where(m =>
            (!_settings.OnlySelectedValues || !string.IsNullOrEmpty(m.SelectionStatus))
            && (!_settings.OnlyMappedMeasurements || _mapper.HasMapping(m))
            && Passes(includeConcepts, excludeConcepts, m.SourceCode, m.Name, m.ShortName)
            && Passes(includeSites, excludeSites, m.FindingSite)
            && Passes(includeModes, excludeModes, m.ImageMode));

        var result = CondenseRepeatedValues(selected.ToList());

        if (_settings.MaxMeasurements > 0 && result.Count > _settings.MaxMeasurements)
            result = result.Take(_settings.MaxMeasurements).ToList();

        return result;
    }

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

        var (value, note) = _settings.RepeatedValues switch
        {
            RepeatedValueMode.Mean => (numeric.Average(m => m.NumericValue!.Value), "Mittel"),
            RepeatedValueMode.Min => (numeric.Min(m => m.NumericValue!.Value), "Minimum"),
            RepeatedValueMode.Max => (numeric.Max(m => m.NumericValue!.Value), "Maximum"),
            _ => (numeric[0].NumericValue!.Value, string.Empty)
        };

        var condensed = group[0].Clone();
        condensed.NumericValue = value;
        condensed.RawValue = value.ToString("R", CultureInfo.InvariantCulture);
        condensed.Value = DicomValueConverter.FormatNumeric(condensed.RawValue, _gdt.DecimalPlaces, _gdt.DecimalSeparator);
        condensed.AggregationNote = $"{note} aus {numeric.Count}";
        return condensed;
    }

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
