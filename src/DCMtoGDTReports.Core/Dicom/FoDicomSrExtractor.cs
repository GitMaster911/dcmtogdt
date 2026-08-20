using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DCMtoGDTReports.Core.Models;
using FellowOakDicom;

namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Eingebautes DICOM-Toolkit auf Basis von fo-dicom. Damit ist keine externe DCMTK-Installation
/// noetig; DCMTK kann optional zusaetzlich konfiguriert werden.
/// </summary>
public sealed class FoDicomSrExtractor : ISrExtractor
{
    static FoDicomSrExtractor()
    {
        // Wird fuer DICOM-Dateien mit ISO-8859-x Zeichensatz benoetigt.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public string EngineName => "Builtin (fo-dicom)";

    public async Task<SrReport> ExtractAsync(string dicomFilePath, string? debugXmlPath = null, CancellationToken ct = default)
    {
        if (!File.Exists(dicomFilePath))
            throw new FileNotFoundException("DICOM-Datei nicht gefunden.", dicomFilePath);

        var dicomFile = await DicomFile.OpenAsync(dicomFilePath).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var dataset = dicomFile.Dataset;
        var header = ReadHeader(dataset);

        var raw = new List<MeasurementResult>();
        var context = new SrNodeContext();
        WalkContentSequence(dataset, context, "/", raw);

        var measurements = MeasurementDeduplicator.Deduplicate(raw);

        var report = new SrReport
        {
            Header = header,
            Measurements = measurements,
            Engine = EngineName,
            RawMeasurementCount = raw.Count
        };

        if (!string.IsNullOrWhiteSpace(debugXmlPath))
        {
            WriteDebugXml(dataset, header, debugXmlPath);
            report.DebugXmlPath = debugXmlPath;
        }

        return report;
    }

    private static SrHeader ReadHeader(DicomDataset ds)
    {
        var header = new SrHeader
        {
            PatientId = Get(ds, DicomTag.PatientID),
            PatientName = Get(ds, DicomTag.PatientName),
            PatientBirthDate = Get(ds, DicomTag.PatientBirthDate),
            PatientSex = Get(ds, DicomTag.PatientSex),
            StudyDate = FirstNonEmpty(Get(ds, DicomTag.StudyDate), Get(ds, DicomTag.ContentDate)),
            StudyTime = FirstNonEmpty(Get(ds, DicomTag.StudyTime), Get(ds, DicomTag.ContentTime)),
            AccessionNumber = Get(ds, DicomTag.AccessionNumber),
            StudyInstanceUid = Get(ds, DicomTag.StudyInstanceUID),
            SeriesInstanceUid = Get(ds, DicomTag.SeriesInstanceUID),
            SopInstanceUid = Get(ds, DicomTag.SOPInstanceUID),
            StudyDescription = CleanDescription(Get(ds, DicomTag.StudyDescription)),
            SeriesDescription = CleanDescription(Get(ds, DicomTag.SeriesDescription)),
            Modality = Get(ds, DicomTag.Modality),
            Manufacturer = Get(ds, DicomTag.Manufacturer),
            ManufacturerModelName = Get(ds, DicomTag.ManufacturerModelName),
            StationName = Get(ds, DicomTag.StationName),
            DocumentTitle = ReadCode(ds, DicomTag.ConceptNameCodeSequence).Meaning
        };

        DicomValueConverter.FillNameParts(header);
        return header;
    }

    /// <summary>
    /// Laeuft rekursiv durch die Content Sequence. Finding Site und Image Mode werden vom
    /// uebergeordneten Container an die Messwerte vererbt (so liefert der Vivid T8 die Daten).
    /// </summary>
    private static void WalkContentSequence(DicomDataset node, SrNodeContext context, string path, List<MeasurementResult> sink)
    {
        if (!node.TryGetSequence(DicomTag.ContentSequence, out var contentSequence))
            return;

        // Erst die Modifikatoren des Containers einsammeln, damit sie fuer alle Kinder gelten.
        var childContext = context.Clone();
        for (var i = 0; i < contentSequence.Items.Count; i++)
            ApplyModifier(contentSequence.Items[i], childContext);

        for (var i = 0; i < contentSequence.Items.Count; i++)
        {
            var item = contentSequence.Items[i];
            var itemPath = $"{path}{i}/";
            var valueType = Get(item, DicomTag.ValueType);
            var relationship = Get(item, DicomTag.RelationshipType);

            if (!string.Equals(relationship, SrConcepts.RelationshipContains, StringComparison.OrdinalIgnoreCase))
                continue; // Modifikatoren wurden oben bereits verarbeitet.

            switch (valueType)
            {
                case SrConcepts.ValueTypeContainer:
                    WalkContentSequence(item, childContext.Clone(), itemPath, sink);
                    break;

                case SrConcepts.ValueTypeNum:
                    var measurement = ReadNumericNode(item, childContext, itemPath);
                    if (measurement is not null) sink.Add(measurement);
                    break;

                default:
                    // Container koennen auch unter anderen Value Types haengen - trotzdem weiterlaufen.
                    WalkContentSequence(item, childContext, itemPath, sink);
                    break;
            }
        }
    }

    private static MeasurementResult? ReadNumericNode(DicomDataset node, SrNodeContext context, string path)
    {
        if (!node.TryGetSequence(DicomTag.MeasuredValueSequence, out var measured) || measured.Items.Count == 0)
            return null;

        var measuredValue = measured.Items[0];
        var rawValue = Get(measuredValue, DicomTag.NumericValue);
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var concept = ReadCode(node, DicomTag.ConceptNameCodeSequence);
        var unit = ReadCode(measuredValue, DicomTag.MeasurementUnitsCodeSequence);

        // Modifikatoren direkt am Messwert ueberschreiben die geerbten Werte des Containers.
        var local = context.Clone();
        if (node.TryGetSequence(DicomTag.ContentSequence, out var modifiers))
        {
            foreach (var modifier in modifiers.Items)
                ApplyModifier(modifier, local);
        }

        var measurement = new MeasurementResult
        {
            Name = concept.Meaning,
            ShortName = concept.Meaning,
            RawValue = rawValue.Trim(),
            Value = rawValue.Trim(),
            Unit = ShortenUnit(unit),
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

    /// <summary>Liest einen CODE-Knoten und legt ihn im Kontext ab, falls er ein bekannter Modifikator ist.</summary>
    private static void ApplyModifier(DicomDataset node, SrNodeContext context)
    {
        if (!string.Equals(Get(node, DicomTag.ValueType), SrConcepts.ValueTypeCode, StringComparison.OrdinalIgnoreCase))
            return;

        var concept = ReadCode(node, DicomTag.ConceptNameCodeSequence);
        var value = ReadCode(node, DicomTag.ConceptCodeSequence);
        if (concept.IsEmpty || value.IsEmpty)
            return;

        if (concept.Matches(SrConcepts.FindingSite)) context.FindingSite = value.Meaning;
        else if (concept.Matches(SrConcepts.MeasurementMethod)) context.Method = value.Meaning;
        else if (concept.Matches(SrConcepts.ImageMode)) context.ImageMode = value.Meaning;
        else if (concept.Matches(SrConcepts.CardiacCyclePoint)) context.CardiacCyclePoint = value.Meaning;
        else if (concept.Matches(SrConcepts.DirectionOfFlow)) context.DirectionOfFlow = value.Meaning;
        else if (concept.Matches(SrConcepts.Derivation)) context.Derivation = value.Meaning;
        else if (concept.Matches(SrConcepts.SelectionStatus)) context.SelectionStatus = value.Meaning;
    }

    private static string BuildGroupLabel(SrNodeContext context)
    {
        var parts = new[] { context.FindingSite, context.ImageMode }.Where(p => !string.IsNullOrWhiteSpace(p));
        var label = string.Join(" / ", parts);
        return string.IsNullOrEmpty(label) ? "Allgemein" : label;
    }

    /// <summary>
    /// UCUM-Kuerzel bevorzugen ("cm" statt "centimeter"), da der Ergebnistext in GDT kurz bleiben muss.
    /// Die UCUM-Einheit "1" steht fuer dimensionslose Verhaeltnisse (z. B. E/A) und wird weggelassen.
    /// </summary>
    private static string ShortenUnit(SrCode unit)
    {
        if (unit.IsEmpty) return string.Empty;

        var value = string.IsNullOrWhiteSpace(unit.Value) ? unit.Meaning : unit.Value;
        return value.Trim() is "1" or "" ? string.Empty : value;
    }

    private static SrCode ReadCode(DicomDataset ds, DicomTag sequenceTag)
    {
        if (!ds.TryGetSequence(sequenceTag, out var sequence) || sequence.Items.Count == 0)
            return SrCode.Empty;

        var item = sequence.Items[0];
        return new SrCode(
            Get(item, DicomTag.CodeValue),
            Get(item, DicomTag.CodingSchemeDesignator),
            Get(item, DicomTag.CodeMeaning));
    }

    private static string Get(DicomDataset ds, DicomTag tag)
    {
        try
        {
            return ds.GetSingleValueOrDefault(tag, string.Empty)?.Trim() ?? string.Empty;
        }
        catch (DicomDataException)
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    /// <summary>Der Vivid T8 sendet gelegentlich Platzhalter wie "*" als Study Description.</summary>
    private static string CleanDescription(string value)
        => value.Trim() is "*" or "-" ? string.Empty : value.Trim();

    /// <summary>Schreibt eine lesbare Struktur-XML zur Fehlersuche (Aequivalent zu dsr2xml).</summary>
    private static void WriteDebugXml(DicomDataset dataset, SrHeader header, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var root = new XElement("report",
            new XAttribute("engine", "fo-dicom"),
            new XElement("header",
                new XElement("PatientID", header.PatientId),
                new XElement("StudyDate", header.StudyDate),
                new XElement("StudyTime", header.StudyTime),
                new XElement("AccessionNumber", header.AccessionNumber),
                new XElement("StudyInstanceUID", header.StudyInstanceUid),
                new XElement("SeriesInstanceUID", header.SeriesInstanceUid),
                new XElement("SOPInstanceUID", header.SopInstanceUid),
                new XElement("Modality", header.Modality),
                new XElement("Manufacturer", header.Manufacturer),
                new XElement("ManufacturerModelName", header.ManufacturerModelName),
                new XElement("StationName", header.StationName)));

        var content = new XElement("content");
        AppendXmlNodes(dataset, content);
        root.Add(content);

        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
    }

    private static void AppendXmlNodes(DicomDataset node, XElement parent)
    {
        if (!node.TryGetSequence(DicomTag.ContentSequence, out var sequence))
            return;

        foreach (var item in sequence.Items)
        {
            var valueType = Get(item, DicomTag.ValueType);
            var element = new XElement("item",
                new XAttribute("relationship", Get(item, DicomTag.RelationshipType)),
                new XAttribute("valueType", string.IsNullOrEmpty(valueType) ? "UNKNOWN" : valueType));

            var concept = ReadCode(item, DicomTag.ConceptNameCodeSequence);
            if (!concept.IsEmpty)
            {
                element.Add(new XElement("concept",
                    new XAttribute("code", concept.Qualified),
                    concept.Meaning));
            }

            switch (valueType)
            {
                case SrConcepts.ValueTypeNum when item.TryGetSequence(DicomTag.MeasuredValueSequence, out var mv) && mv.Items.Count > 0:
                    var unit = ReadCode(mv.Items[0], DicomTag.MeasurementUnitsCodeSequence);
                    element.Add(new XElement("value", Get(mv.Items[0], DicomTag.NumericValue)));
                    element.Add(new XElement("unit", new XAttribute("code", unit.Qualified), unit.Meaning));
                    break;

                case SrConcepts.ValueTypeCode:
                    var code = ReadCode(item, DicomTag.ConceptCodeSequence);
                    element.Add(new XElement("code", new XAttribute("code", code.Qualified), code.Meaning));
                    break;
            }

            AppendXmlNodes(item, element);
            parent.Add(element);
        }
    }

    /// <summary>Geerbter Kontext waehrend des Baumdurchlaufs.</summary>
    private sealed class SrNodeContext
    {
        public string FindingSite { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string ImageMode { get; set; } = string.Empty;
        public string CardiacCyclePoint { get; set; } = string.Empty;
        public string DirectionOfFlow { get; set; } = string.Empty;
        public string Derivation { get; set; } = string.Empty;
        public string SelectionStatus { get; set; } = string.Empty;

        public SrNodeContext Clone() => (SrNodeContext)MemberwiseClone();
    }
}
