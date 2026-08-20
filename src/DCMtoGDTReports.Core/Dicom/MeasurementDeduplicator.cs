using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Der Vivid T8 schreibt jeden Messwert doppelt in die Measurement Group: einmal mit
/// Selection Status (der vom Geraet gewaehlte Wert) und einmal ohne. Diese Klasse entfernt
/// die Dubletten und behaelt bevorzugt den Eintrag mit Selection Status.
/// </summary>
public static class MeasurementDeduplicator
{
    public static IReadOnlyList<MeasurementResult> Deduplicate(IEnumerable<MeasurementResult> measurements)
    {
        var result = new List<MeasurementResult>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var measurement in measurements)
        {
            var key = BuildKey(measurement);
            if (!indexByKey.TryGetValue(key, out var existingIndex))
            {
                indexByKey[key] = result.Count;
                result.Add(measurement);
                continue;
            }

            // Bereits vorhanden: den Eintrag mit Selection Status bevorzugen.
            var existing = result[existingIndex];
            if (string.IsNullOrEmpty(existing.SelectionStatus) && !string.IsNullOrEmpty(measurement.SelectionStatus))
                result[existingIndex] = measurement;
        }

        return result;
    }

    private static string BuildKey(MeasurementResult m) => string.Join('|',
        m.SourceCode,
        m.Name,
        m.FindingSite,
        m.Method,
        m.CardiacCyclePoint,
        m.DirectionOfFlow,
        m.ImageMode,
        m.RawValue,
        m.Unit);
}
