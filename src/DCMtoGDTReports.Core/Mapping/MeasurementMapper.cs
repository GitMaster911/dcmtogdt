using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Mapping;

/// <summary>
/// Ordnet den SR-Bezeichnungen kurze, in der Praxis gebraeuchliche Namen zu und formatiert die Werte.
/// Der Schluessel darf entweder das Code Meaning oder der Concept-Code ("LN:29436-3") sein.
/// Ohne Treffer bleibt der Originalname aus dem SR erhalten.
/// </summary>
public sealed class MeasurementMapper
{
    private readonly Dictionary<string, string> _map;
    private readonly Dictionary<string, string> _methods;

    public MeasurementMapper(
        IReadOnlyDictionary<string, string>? userMappings = null,
        IReadOnlyDictionary<string, string>? methodMappings = null)
    {
        _map = new Dictionary<string, string>(DefaultShortNames, StringComparer.OrdinalIgnoreCase);
        _methods = new Dictionary<string, string>(DefaultMethodNames, StringComparer.OrdinalIgnoreCase);

        // Benutzerdefinierte Eintraege ueberschreiben die Standardzuordnung.
        Merge(_map, userMappings);
        Merge(_methods, methodMappings);
    }

    private static void Merge(Dictionary<string, string> target, IReadOnlyDictionary<string, string>? source)
    {
        if (source is null) return;
        foreach (var (key, value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key)) target[key.Trim()] = value;
        }
    }

    /// <summary>Setzt Kurzname, gekuerzte Methode und formatierten Wert auf allen Messwerten.</summary>
    public void Apply(IEnumerable<MeasurementResult> measurements, GdtSettings settings)
    {
        foreach (var measurement in measurements)
        {
            measurement.ShortName = ResolveShortName(measurement);
            measurement.Method = ResolveMethod(measurement.Method);
            measurement.Value = DicomValueConverter.FormatNumeric(
                measurement.RawValue, settings.DecimalPlaces, settings.DecimalSeparator);
        }
    }

    /// <summary>Kuerzt die Messmethode, falls eine Abkuerzung hinterlegt ist.</summary>
    public string ResolveMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method)) return string.Empty;
        return _methods.TryGetValue(method, out var mapped) ? mapped : method;
    }

    public string ResolveShortName(MeasurementResult measurement)
    {
        if (!string.IsNullOrEmpty(measurement.SourceCode) && _map.TryGetValue(measurement.SourceCode, out var byCode))
            return byCode;

        return _map.TryGetValue(measurement.Name, out var byName) ? byName : measurement.Name;
    }

    /// <summary>Prueft, ob fuer den Messwert ueberhaupt eine Kurzbezeichnung hinterlegt ist.</summary>
    public bool HasMapping(MeasurementResult measurement)
        => (!string.IsNullOrEmpty(measurement.SourceCode) && _map.ContainsKey(measurement.SourceCode))
           || (!string.IsNullOrEmpty(measurement.Name) && _map.ContainsKey(measurement.Name));

    /// <summary>
    /// Standardzuordnung, abgeleitet aus den Concept Names, die der GE Vivid T8 im
    /// Adult Echocardiography Procedure Report tatsaechlich liefert.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultShortNames { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Linker Ventrikel - Dimensionen
            ["Left Ventricle Internal End Diastolic Dimension"] = "LVIDd",
            ["Left Ventricular Internal Diastolic Dimension"] = "LVIDd",
            ["Left Ventricle Internal Systolic Dimension"] = "LVIDs",
            ["Interventricular Septum Diastolic Thickness"] = "IVSd",
            ["Interventricular Septal Thickness at End Diastole"] = "IVSd",
            ["Left Ventricle Posterior Wall Diastolic Thickness"] = "LVPWd",
            ["Right Ventricular Internal Diastolic Dimension"] = "RVIDd",
            ["Left Ventricle Mass"] = "LV-Masse",

            // Linker Ventrikel - Volumina und Funktion
            ["Left Ventricular End Diastolic Volume"] = "EDV",
            ["Left Ventricular End Systolic Volume"] = "ESV",
            ["Left Ventricular Ejection Fraction"] = "EF",
            ["Stroke Volume"] = "SV",
            ["Cardiac Output"] = "HZV",
            ["Heart rate"] = "HF",
            ["Left Ventricular Major Axis Diastolic Dimension, 2-chamber view"] = "LV Laenge d (2CH)",
            ["Left Ventricular Major Axis Diastolic Dimension, 4-chamber view"] = "LV Laenge d (4CH)",
            ["Left Ventricular Major Axis Systolic Dimension, 2-chamber view"] = "LV Laenge s (2CH)",
            ["Left Ventricular Major Axis Systolic Dimension, 4-chamber view"] = "LV Laenge s (4CH)",

            // Aorta
            ["Aortic Root Diameter"] = "Ao-Wurzel",
            ["Ascending Aortic Diameter"] = "Ao asc.",

            // Klappen / Doppler
            ["Peak Velocity"] = "Vmax",
            ["Mean Velocity"] = "Vmean",
            ["Peak Gradient"] = "maxPG",
            ["Mean Gradient"] = "mPG",
            ["Velocity Time Integral"] = "VTI",
            ["Cardiovascular Orifice Diameter"] = "Durchmesser",
            ["Cardiovascular Orifice Area"] = "Flaeche",
            ["Diameter"] = "Durchmesser",
            ["Acceleration Time"] = "AccT",
            ["Acceleration Slope"] = "AccSlope",
            ["Deceleration Time"] = "DecT",
            ["Deceleration Slope"] = "DecSlope",

            // Mitralklappe / Diastolische Funktion
            ["Mitral Valve E-Wave Peak Velocity"] = "MV E",
            ["Mitral Valve A-Wave Peak Velocity"] = "MV A",
            ["Mitral Valve E to A Ratio"] = "MV E/A",
            ["Average Annulus E Velocity"] = "e' mittel",
            ["E Velocity to Annulus E Velocity Ratio"] = "E/e'",
            ["E Velocity to Average Annulus E Velocity"] = "E/e' mittel",
            ["Peak Tissue Velocity"] = "Gewebe Vmax",

            // Rechter Ventrikel
            ["Tricuspid Annular Plane Systolic Excursion (TAPSE)"] = "TAPSE",

            // Strain (AFI)
            ["Global Peak Longitudinal Strain"] = "GLPS",
            ["Peak Longitudinal Strain"] = "PLS",
            ["Peak Strain Dispersion"] = "PSD",
            ["Aortic Valve Closure"] = "AVC",
            ["Time duration of the VTI trace on Aortic Valve"] = "AV VTI-Dauer",
            ["Time duration of the VTI trace on LVOT"] = "LVOT VTI-Dauer"
        };

    /// <summary>
    /// Abkuerzungen fuer die teils sehr langen Methodenbezeichnungen des Vivid T8.
    /// Ohne sie wuerde eine einzige Ergebniszeile ueber mehrere GDT-Zeilen umgebrochen.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultMethodNames { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AFI with 18 segments following 2015 ASE recommendations"] = "AFI 18 Segm.",
            ["AFI with 17 segments following 2015 ASE recommendations"] = "AFI 17 Segm.",
            ["AFI with 16 segments following 2015 ASE recommendations"] = "AFI 16 Segm.",
            ["Left Ventricle Mass by M-mode"] = "M-Mode",
            ["Left Ventricle Mass by Truncated Ellipse"] = "Trunk. Ellipse",
            ["Continuity Equation by Peak Velocity"] = "Kontinuitaet Vmax",
            ["Continuity Equation by Velocity Time Integral"] = "Kontinuitaet VTI",
            ["Continuity Equation by Mean Velocity"] = "Kontinuitaet Vmean",
            ["Modified Simpson"] = "Simpson",
            ["Single Plane Ellipse"] = "Einzelebene",
            ["Area length"] = "Flaeche/Laenge"
        };
}
