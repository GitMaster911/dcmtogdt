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
    private readonly Dictionary<string, string> _regions;
    private readonly Dictionary<string, string> _imageModes;
    private readonly Dictionary<string, string> _flowDirections;

    public MeasurementMapper(
        IReadOnlyDictionary<string, string>? userMappings = null,
        IReadOnlyDictionary<string, string>? methodMappings = null,
        IReadOnlyDictionary<string, string>? regionMappings = null,
        IReadOnlyDictionary<string, string>? imageModeMappings = null,
        IReadOnlyDictionary<string, string>? flowMappings = null)
    {
        _map = new Dictionary<string, string>(DefaultShortNames, StringComparer.OrdinalIgnoreCase);
        _methods = new Dictionary<string, string>(DefaultMethodNames, StringComparer.OrdinalIgnoreCase);
        _regions = new Dictionary<string, string>(DefaultRegionNames, StringComparer.OrdinalIgnoreCase);
        _imageModes = new Dictionary<string, string>(DefaultImageModeNames, StringComparer.OrdinalIgnoreCase);
        _flowDirections = new Dictionary<string, string>(DefaultFlowDirectionNames, StringComparer.OrdinalIgnoreCase);

        // Benutzerdefinierte Eintraege ueberschreiben die Standardzuordnung.
        Merge(_map, userMappings);
        Merge(_methods, methodMappings);
        Merge(_regions, regionMappings);
        Merge(_imageModes, imageModeMappings);
        Merge(_flowDirections, flowMappings);
    }

    private static void Merge(Dictionary<string, string> target, IReadOnlyDictionary<string, string>? source)
    {
        if (source is null) return;
        foreach (var (key, value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key)) target[key.Trim()] = value;
        }
    }

    /// <summary>
    /// Setzt Kurzname, gekuerzte Methode und formatierten Wert. Regionen und Modi bleiben
    /// hier bewusst im englischen Original, weil Katalog und Filter darauf aufbauen.
    /// </summary>
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

    /// <summary>
    /// Uebersetzt Region, Aufnahmemodus und Flussrichtung ins Deutsche und bildet die
    /// Gruppenbezeichnung neu. Laeuft erst nach dem Filtern, damit Katalog und Filter
    /// weiterhin mit den DICOM-Originalbezeichnungen arbeiten.
    /// </summary>
    public void ApplyGermanNames(IEnumerable<MeasurementResult> measurements)
    {
        foreach (var measurement in measurements)
        {
            measurement.FindingSite = ResolveRegion(measurement.FindingSite);
            measurement.ImageMode = ResolveImageMode(measurement.ImageMode);
            measurement.DirectionOfFlow = ResolveFlowDirection(measurement.DirectionOfFlow);
            measurement.Group = BuildGroupLabel(measurement);
        }
    }

    /// <summary>Kuerzt die Messmethode, falls eine Abkuerzung hinterlegt ist.</summary>
    public string ResolveMethod(string method) => Lookup(_methods, method);

    /// <summary>Deutsche Bezeichnung der anatomischen Region.</summary>
    public string ResolveRegion(string region) => Lookup(_regions, region);

    /// <summary>Deutsche Bezeichnung des Aufnahmemodus.</summary>
    public string ResolveImageMode(string imageMode) => Lookup(_imageModes, imageMode);

    /// <summary>Deutsche Bezeichnung der Flussrichtung.</summary>
    public string ResolveFlowDirection(string flow) => Lookup(_flowDirections, flow);

    private static string Lookup(Dictionary<string, string> map, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return map.TryGetValue(value, out var mapped) ? mapped : value;
    }

    private static string BuildGroupLabel(MeasurementResult measurement)
    {
        var parts = new[] { measurement.FindingSite, measurement.ImageMode }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var label = string.Join(" / ", parts);
        return string.IsNullOrEmpty(label) ? "Allgemein" : label;
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

    /// <summary>
    /// Deutsche Bezeichnungen der anatomischen Regionen. Der DICOM-Bericht liefert nur
    /// englische Texte; im Krankenblatt sind deutsche Bezeichnungen deutlich lesbarer.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultRegionNames { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Herzhoehlen und Ausflusstrakte
            ["Left Ventricle"] = "Linker Ventrikel",
            ["Right Ventricle"] = "Rechter Ventrikel",
            ["Left Atrium"] = "Linker Vorhof",
            ["Right Atrium"] = "Rechter Vorhof",
            ["Left Ventricle Outflow Tract"] = "LV-Ausflusstrakt",
            ["Right Ventricle Outflow Tract"] = "RV-Ausflusstrakt",
            ["Interventricular Septum"] = "Kammerseptum",

            // Klappen und Klappenringe
            ["Aortic Valve"] = "Aortenklappe",
            ["Mitral Valve"] = "Mitralklappe",
            ["Pulmonic Valve"] = "Pulmonalklappe",
            ["Tricuspid Valve"] = "Trikuspidalklappe",
            ["Lateral Mitral Annulus"] = "Mitralklappenring lateral",
            ["Medial Mitral Annulus"] = "Mitralklappenring septal",
            ["Septal Mitral Annulus"] = "Mitralklappenring septal",
            ["Tricuspid Annulus"] = "Trikuspidalklappenring",

            // Gefaesse
            ["Aorta"] = "Aorta",
            ["Ascending Aorta"] = "Aorta ascendens",
            ["Aortic Root"] = "Aortenwurzel",
            ["Pulmonary Artery"] = "Pulmonalarterie",
            ["Inferior Vena Cava"] = "Vena cava inferior",

            // 18 Strain-Segmente des linken Ventrikels
            ["left ventricle basal anterior segment"] = "LV basal anterior",
            ["left ventricle basal anteroseptal segment"] = "LV basal anteroseptal",
            ["left ventricle basal inferoseptal segment"] = "LV basal inferoseptal",
            ["left ventricle basal inferior segment"] = "LV basal inferior",
            ["left ventricle basal inferolateral segment"] = "LV basal inferolateral",
            ["left ventricle basal anterolateral segment"] = "LV basal anterolateral",
            ["left ventricle mid anterior segment"] = "LV midventrikulaer anterior",
            ["left ventricle mid anteroseptal segment"] = "LV midventrikulaer anteroseptal",
            ["left ventricle mid inferoseptal segment"] = "LV midventrikulaer inferoseptal",
            ["left ventricle mid inferior segment"] = "LV midventrikulaer inferior",
            ["left ventricle mid inferolateral segment"] = "LV midventrikulaer inferolateral",
            ["left ventricle mid anterolateral segment"] = "LV midventrikulaer anterolateral",
            ["left ventricle apical anterior segment"] = "LV apikal anterior",
            ["left ventricle apical anteroseptal segment"] = "LV apikal anteroseptal",
            ["left ventricle apical inferoseptal segment"] = "LV apikal inferoseptal",
            ["left ventricle apical inferior segment"] = "LV apikal inferior",
            ["left ventricle apical inferolateral segment"] = "LV apikal inferolateral",
            ["left ventricle apical anterolateral segment"] = "LV apikal anterolateral",
            ["left ventricle apex"] = "LV Apex"
        };

    /// <summary>Deutsche Bezeichnungen der Aufnahmemodi.</summary>
    public static IReadOnlyDictionary<string, string> DefaultImageModeNames { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["2D mode"] = "2D",
            ["M mode"] = "M-Mode",
            ["Doppler Pulsed"] = "PW-Doppler",
            ["Doppler Continuous Wave"] = "CW-Doppler",
            ["Doppler Color"] = "Farbdoppler",
            ["Doppler Tissue"] = "Gewebedoppler",
            ["Tissue Doppler Imaging"] = "Gewebedoppler",
            ["Doppler Tissue Velocity"] = "Gewebedoppler"
        };

    /// <summary>Deutsche Bezeichnungen der Flussrichtung.</summary>
    public static IReadOnlyDictionary<string, string> DefaultFlowDirectionNames { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Antegrade Flow"] = "antegrad",
            ["Retrograde Flow"] = "retrograd",
            ["Regurgitant Flow"] = "Regurgitation"
        };
}
