using System.Globalization;
using System.Xml.Linq;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Parst die von dsr2xml erzeugte XML-Struktur. Der Parser arbeitet rekursiv und bewusst
/// tolerant, weil dsr2xml je nach DCMTK-Version leicht unterschiedliche Elementnamen liefert.
/// </summary>
public static class DsrXmlParser
{
    public static SrReport Parse(string xmlPath, string engineName)
    {
        var document = XDocument.Load(xmlPath, LoadOptions.None);
        return Parse(document, engineName);
    }

    public static SrReport Parse(XDocument document, string engineName)
    {
        var root = document.Root ?? throw new InvalidOperationException("Die dsr2xml-Ausgabe enthaelt kein Wurzelelement.");

        var header = ReadHeader(root);
        var contentRoot = FindContentRoot(root);

        var raw = new List<MeasurementResult>();
        if (contentRoot is not null)
        {
            header.DocumentTitle = ReadConcept(contentRoot).Meaning;
            WalkChildren(contentRoot, new XmlNodeContext(), "/", raw);
        }

        return new SrReport
        {
            Header = header,
            Measurements = MeasurementDeduplicator.Deduplicate(raw),
            Engine = engineName,
            RawMeasurementCount = raw.Count,
            DebugXmlPath = null
        };
    }

    private static SrHeader ReadHeader(XElement root)
    {
        var patient = FindElement(root, "patient");
        var study = FindElement(root, "study");
        var series = FindElement(root, "series");
        var instance = FindElement(root, "instance");
        var device = FindElement(root, "device") ?? FindElement(root, "equipment");

        var nameElement = patient is null ? null : FindElement(patient, "name");
        var lastName = Text(nameElement, "last", "family");
        var firstName = Text(nameElement, "first", "given");

        var header = new SrHeader
        {
            PatientId = Text(patient, "id"),
            PatientBirthDate = DigitsOnly(Text(FindElement(patient, "birthdate"), "date") is { Length: > 0 } b ? b : Text(patient, "birthdate")),
            PatientSex = Text(patient, "sex"),
            LastName = lastName,
            FirstName = firstName,
            StudyDate = DigitsOnly(Text(study, "date")),
            StudyTime = Text(study, "time"),
            AccessionNumber = Text(FindElement(study, "accession"), "number") is { Length: > 0 } a ? a : Text(study, "accession"),
            StudyInstanceUid = Attr(study, "uid"),
            SeriesInstanceUid = Attr(series, "uid"),
            SopInstanceUid = Attr(instance, "uid"),
            StudyDescription = Text(study, "description"),
            SeriesDescription = Text(series, "description"),
            Modality = Text(series, "modality"),
            Manufacturer = Text(device, "manufacturer"),
            ManufacturerModelName = Text(device, "model", "modelname"),
            StationName = Text(device, "station", "stationname")
        };

        header.PatientName = string.IsNullOrEmpty(firstName) ? lastName : $"{lastName}^{firstName}";
        return header;
    }

    private static XElement? FindContentRoot(XElement root)
    {
        var content = FindElement(root, "content");
        if (content is null) return null;

        // dsr2xml verpackt den Wurzelcontainer je nach Version direkt unter <content>.
        var container = content.Elements().FirstOrDefault(e => IsName(e, "container"));
        return container ?? content;
    }

    private static void WalkChildren(XElement node, XmlNodeContext context, string path, List<MeasurementResult> sink)
    {
        var childContext = context.Clone();
        foreach (var child in node.Elements())
            ApplyModifier(child, childContext);

        var index = 0;
        foreach (var child in node.Elements())
        {
            if (!IsContentItem(child)) continue;

            var itemPath = $"{path}{index++}/";
            if (!IsRelationship(child, "contains")) continue;

            if (IsName(child, "container"))
            {
                WalkChildren(child, childContext.Clone(), itemPath, sink);
            }
            else if (IsName(child, "num"))
            {
                var measurement = ReadNumeric(child, childContext, itemPath);
                if (measurement is not null) sink.Add(measurement);
            }
            else
            {
                WalkChildren(child, childContext.Clone(), itemPath, sink);
            }
        }
    }

    private static MeasurementResult? ReadNumeric(XElement node, XmlNodeContext context, string path)
    {
        var rawValue = Text(node, "value");
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var concept = ReadConcept(node);
        var unitElement = FindElement(node, "unit");
        var unit = unitElement is null
            ? string.Empty
            : Text(unitElement, "value") is { Length: > 0 } uv ? uv : Text(unitElement, "meaning");

        var local = context.Clone();
        foreach (var child in node.Elements())
            ApplyModifier(child, local);

        var measurement = new MeasurementResult
        {
            Name = concept.Meaning,
            ShortName = concept.Meaning,
            RawValue = rawValue.Trim(),
            Value = rawValue.Trim(),
            Unit = unit,
            FindingSite = local.FindingSite,
            Method = local.Method,
            ImageMode = local.ImageMode,
            CardiacCyclePoint = local.CardiacCyclePoint,
            DirectionOfFlow = local.DirectionOfFlow,
            Derivation = local.Derivation,
            SelectionStatus = local.SelectionStatus,
            SourceCode = concept.Qualified,
            RawPath = path.TrimEnd('/'),
            Group = BuildGroupLabel(local)
        };

        if (double.TryParse(measurement.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            measurement.NumericValue = numeric;

        return measurement;
    }

    private static void ApplyModifier(XElement node, XmlNodeContext context)
    {
        if (!IsName(node, "code")) return;

        var concept = ReadConcept(node);
        if (concept.IsEmpty) return;

        // Bei <code> stehen Wert und Bedeutung des Codes direkt im Element.
        var meaning = Text(node, "meaning");
        if (string.IsNullOrEmpty(meaning)) return;

        if (concept.Matches(SrConcepts.FindingSite)) context.FindingSite = meaning;
        else if (concept.Matches(SrConcepts.MeasurementMethod)) context.Method = meaning;
        else if (concept.Matches(SrConcepts.ImageMode)) context.ImageMode = meaning;
        else if (concept.Matches(SrConcepts.CardiacCyclePoint)) context.CardiacCyclePoint = meaning;
        else if (concept.Matches(SrConcepts.DirectionOfFlow)) context.DirectionOfFlow = meaning;
        else if (concept.Matches(SrConcepts.Derivation)) context.Derivation = meaning;
        else if (concept.Matches(SrConcepts.SelectionStatus)) context.SelectionStatus = meaning;
    }

    private static SrCode ReadConcept(XElement node)
    {
        var concept = FindElement(node, "concept");
        if (concept is null) return SrCode.Empty;

        return new SrCode(Text(concept, "value"), Text(concept, "scheme", "schemedesignator"), Text(concept, "meaning"));
    }

    private static string BuildGroupLabel(XmlNodeContext context)
    {
        var label = string.Join(" / ", new[] { context.FindingSite, context.ImageMode }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrEmpty(label) ? "Allgemein" : label;
    }

    private static bool IsContentItem(XElement element) =>
        IsName(element, "container", "num", "code", "text", "date", "time", "pname", "uidref", "composite", "image", "waveform", "scoord", "tcoord");

    private static bool IsRelationship(XElement element, string relationship)
    {
        var value = element.Attribute("relationship")?.Value;
        // Der Wurzelcontainer hat kein relationship-Attribut - den behandeln wir wie "contains".
        return string.IsNullOrEmpty(value)
            || string.Equals(value.Replace("-", " "), relationship, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsName(XElement element, params string[] names) =>
        names.Any(n => string.Equals(element.Name.LocalName, n, StringComparison.OrdinalIgnoreCase));

    private static XElement? FindElement(XElement? parent, params string[] names) =>
        parent?.Elements().FirstOrDefault(e => IsName(e, names));

    private static string Text(XElement? parent, params string[] names)
        => FindElement(parent, names)?.Value.Trim() ?? string.Empty;

    private static string Attr(XElement? element, string name)
        => element?.Attribute(name)?.Value.Trim() ?? string.Empty;

    private static string DigitsOnly(string value)
        => new(value.Where(char.IsDigit).ToArray());

    private sealed class XmlNodeContext
    {
        public string FindingSite { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string ImageMode { get; set; } = string.Empty;
        public string CardiacCyclePoint { get; set; } = string.Empty;
        public string DirectionOfFlow { get; set; } = string.Empty;
        public string Derivation { get; set; } = string.Empty;
        public string SelectionStatus { get; set; } = string.Empty;

        public XmlNodeContext Clone() => (XmlNodeContext)MemberwiseClone();
    }
}
