using System.Globalization;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Konvertierungen zwischen DICOM-Wertformaten und den von GDT erwarteten Formaten.
/// </summary>
public static class DicomValueConverter
{
    /// <summary>
    /// DICOM DA (YYYYMMDD) -> GDT (DDMMYYYY). Liefert leer, wenn kein gueltiges Datum vorliegt.
    /// </summary>
    public static string ToGdtDate(string? dicomDate)
    {
        var normalized = KeepDigits(dicomDate);
        if (normalized.Length < 8) return string.Empty;

        return DateTime.TryParseExact(normalized[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date.ToString("ddMMyyyy", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    /// <summary>
    /// DICOM TM (HHMMSS[.FFFFFF]) -> GDT (HHMMSS). Fehlende Sekunden/Minuten werden mit 00 aufgefuellt.
    /// </summary>
    public static string ToGdtTime(string? dicomTime)
    {
        var normalized = KeepDigits(dicomTime?.Split('.')[0]);
        if (normalized.Length == 0) return string.Empty;
        if (normalized.Length > 6) normalized = normalized[..6];

        normalized = normalized.PadRight(6, '0');
        return int.TryParse(normalized[..2], out var h) && h < 24 ? normalized : string.Empty;
    }

    /// <summary>DICOM DA -> Anzeigeformat DD.MM.YYYY.</summary>
    public static string ToDisplayDate(string? dicomDate)
    {
        var gdt = ToGdtDate(dicomDate);
        return gdt.Length == 8 ? $"{gdt[..2]}.{gdt[2..4]}.{gdt[4..]}" : string.Empty;
    }

    /// <summary>DICOM TM -> Anzeigeformat HH:MM:SS.</summary>
    public static string ToDisplayTime(string? dicomTime)
    {
        var gdt = ToGdtTime(dicomTime);
        return gdt.Length == 6 ? $"{gdt[..2]}:{gdt[2..4]}:{gdt[4..]}" : string.Empty;
    }

    /// <summary>
    /// DICOM CS Patient Sex -> GDT Feld 3110 (1 = maennlich, 2 = weiblich).
    /// Alles andere liefert leer, damit kein falsches Geschlecht uebertragen wird.
    /// </summary>
    public static string ToGdtSex(string? dicomSex) => dicomSex?.Trim().ToUpperInvariant() switch
    {
        "M" => "1",
        "F" or "W" => "2",
        _ => string.Empty
    };

    /// <summary>
    /// Zerlegt einen DICOM-Personennamen (Family^Given^Middle^Prefix^Suffix).
    /// Nur wenn die Zerlegung eindeutig ist, werden Vor- und Nachname getrennt gefuellt.
    /// </summary>
    public static (string LastName, string FirstName) ParsePersonName(string? dicomPersonName)
    {
        if (string.IsNullOrWhiteSpace(dicomPersonName))
            return (string.Empty, string.Empty);

        // Mehrbyte-/Ideografische Komponentengruppen (getrennt durch '=') werden ignoriert.
        var alphabetic = dicomPersonName.Split('=')[0].Trim();
        if (alphabetic.Length == 0) return (string.Empty, string.Empty);

        if (!alphabetic.Contains('^'))
        {
            // Ohne Trennzeichen ist keine sichere Zerlegung moeglich - alles als Nachname uebernehmen.
            return (alphabetic.Trim(), string.Empty);
        }

        var parts = alphabetic.Split('^');
        var last = parts.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
        var givenParts = new[] { parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2) }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        return (last, string.Join(" ", givenParts));
    }

    /// <summary>
    /// Formatiert einen SR-Rohwert fuer die Ausgabe: runden, ueberfluessige Nullen entfernen,
    /// Dezimaltrennzeichen anwenden.
    /// </summary>
    public static string FormatNumeric(string? rawValue, int decimalPlaces, string decimalSeparator)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;

        var trimmed = rawValue.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return trimmed; // Nicht-numerische Werte unveraendert durchreichen.

        var rounded = Math.Round(value, Math.Clamp(decimalPlaces, 0, 6), MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.######", CultureInfo.InvariantCulture);
        return decimalSeparator == "." ? text : text.Replace(".", decimalSeparator, StringComparison.Ordinal);
    }

    /// <summary>Zusammengesetzter Datums-/Zeitwert der Untersuchung, falls ermittelbar.</summary>
    public static DateTime? ToDateTime(string? dicomDate, string? dicomTime)
    {
        var d = KeepDigits(dicomDate);
        if (d.Length < 8) return null;
        if (!DateTime.TryParseExact(d[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        var t = ToGdtTime(dicomTime);
        if (t.Length != 6) return date;

        return date
            .AddHours(int.Parse(t[..2], CultureInfo.InvariantCulture))
            .AddMinutes(int.Parse(t[2..4], CultureInfo.InvariantCulture))
            .AddSeconds(int.Parse(t[4..], CultureInfo.InvariantCulture));
    }

    /// <summary>Ergaenzt Vor-/Nachname im Header aus dem PN-Rohwert.</summary>
    public static void FillNameParts(SrHeader header)
    {
        var (last, first) = ParsePersonName(header.PatientName);
        header.LastName = last;
        header.FirstName = first;
    }

    private static string KeepDigits(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());
}
