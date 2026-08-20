using System.Text;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Gdt;

/// <summary>
/// Baut aus den SR-Daten den lesbaren Ergebnistext, der in Feld 6220 uebertragen wird.
/// </summary>
public sealed class GdtReportComposer(GdtSettings settings)
{
    private readonly GdtSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public IReadOnlyList<string> BuildResultLines(SrReport report)
    {
        var header = report.Header;
        var lines = new List<string>();

        var title = string.Join(" ", new[] { _settings.ReportTitle, header.DeviceDescription }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        lines.Add(title);
        lines.Add(string.Empty);

        lines.Add("Untersuchung:");
        AddIfPresent(lines, "Datum", DicomValueConverter.ToDisplayDate(header.StudyDate));
        AddIfPresent(lines, "Uhrzeit", DicomValueConverter.ToDisplayTime(header.StudyTime));
        AddIfPresent(lines, "Accession", header.AccessionNumber);
        AddIfPresent(lines, "Study", FirstNonEmpty(header.StudyDescription, header.DocumentTitle));
        AddIfPresent(lines, "Geraet", FirstNonEmpty(header.StationName, header.ManufacturerModelName));
        lines.Add(string.Empty);

        lines.Add("Messwerte:");
        if (report.Measurements.Count == 0)
        {
            lines.Add("Keine numerischen Messwerte im Structured Report enthalten.");
        }
        else
        {
            // Nach Finding Site / Image Mode gruppieren, damit der Krankenblatteintrag lesbar bleibt.
            foreach (var group in report.Measurements.GroupBy(m => m.Group, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"[{group.Key}]");
                foreach (var measurement in group)
                    lines.Add(measurement.ToDisplayLine());
                lines.Add(string.Empty);
            }

            if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        }

        return WrapLines(lines);
    }

    /// <summary>Kommentarzeilen fuer Feld 6227 - Quellenangabe zur Nachvollziehbarkeit.</summary>
    public IReadOnlyList<string> BuildCommentLines(SrReport report)
    {
        if (!_settings.IncludeSourceComment)
            return [];

        var lines = new List<string>
        {
            "Quelle: DICOM Structured Report",
            $"SOPInstanceUID: {report.Header.SopInstanceUid}"
        };

        if (!string.IsNullOrWhiteSpace(report.Header.StudyInstanceUid))
            lines.Add($"StudyInstanceUID: {report.Header.StudyInstanceUid}");

        return WrapLines(lines);
    }

    /// <summary>
    /// Teilt zu lange Zeilen an Wortgrenzen auf. Fortsetzungszeilen werden eingerueckt,
    /// damit im Krankenblatt erkennbar bleibt, dass sie zusammengehoeren.
    /// </summary>
    public IReadOnlyList<string> WrapLines(IEnumerable<string> lines)
    {
        var maxLength = Math.Clamp(_settings.MaxResultLineLength, 10, GdtField.MaxContentLength);
        var result = new List<string>();

        foreach (var line in lines)
        {
            if (line.Length <= maxLength)
            {
                result.Add(line);
                continue;
            }

            var remaining = line;
            var continuation = false;
            while (remaining.Length > 0)
            {
                var limit = continuation ? maxLength - 2 : maxLength;
                if (remaining.Length <= limit)
                {
                    result.Add(continuation ? "  " + remaining : remaining);
                    break;
                }

                var breakIndex = remaining.LastIndexOf(' ', Math.Min(limit, remaining.Length - 1));
                if (breakIndex <= 0) breakIndex = limit; // Kein Leerzeichen gefunden - hart trennen.

                var chunk = remaining[..breakIndex].TrimEnd();
                result.Add(continuation ? "  " + chunk : chunk);
                remaining = remaining[breakIndex..].TrimStart();
                continuation = true;
            }
        }

        return result;
    }

    private static void AddIfPresent(List<string> lines, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add($"{label}: {value}");
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
