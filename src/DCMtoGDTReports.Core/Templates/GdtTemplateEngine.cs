using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Templates;

/// <summary>Ein Platzhalter samt Klartextbeschreibung fuer die Anzeige im Vorlagen-Editor.</summary>
public sealed record GdtPlaceholder(string Name, string Description, string Example)
{
    public string Token => "{" + Name + "}";
}

/// <summary>
/// Loest die Platzhalter einer GDT-Vorlage auf und erzeugt daraus die Feldliste.
/// </summary>
public sealed class GdtTemplateEngine(GdtSettings settings)
{
    private readonly GdtSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly GdtReportComposer _composer = new(settings);

    /// <summary>Platzhalter, die mehrere Zeilen erzeugen, statt Text zu ersetzen.</summary>
    public const string MeasurementsPlaceholder = "Messwerte";

    public const string MeasurementsFlatPlaceholder = "MesswerteOhneGruppen";

    /// <summary>Alle verfuegbaren Platzhalter - wird im Editor als Auswahlliste angezeigt.</summary>
    public static IReadOnlyList<GdtPlaceholder> Placeholders { get; } =
    [
        new("PatientNummer", "Patientennummer aus dem DICOM-Header", "12345"),
        new("Nachname", "Nachname des Patienten", "Muster"),
        new("Vorname", "Vorname des Patienten", "Erika"),
        new("Geburtsdatum", "Geburtsdatum im GDT-Format TTMMJJJJ", "12031985"),
        new("GeburtsdatumLang", "Geburtsdatum lesbar TT.MM.JJJJ", "12.03.1985"),
        new("Geschlecht", "Geschlecht als GDT-Code (1 = maennlich, 2 = weiblich)", "2"),
        new("Untersuchungsdatum", "Untersuchungsdatum im GDT-Format TTMMJJJJ", "15012024"),
        new("Untersuchungszeit", "Untersuchungszeit im GDT-Format HHMMSS", "103015"),
        new("DatumLang", "Untersuchungsdatum lesbar TT.MM.JJJJ", "15.01.2024"),
        new("ZeitLang", "Untersuchungszeit lesbar HH:MM:SS", "10:30:15"),
        new("Anforderungsnummer", "Accession Number der Untersuchung", "1000042"),
        new("Geraet", "Stationsname bzw. Geraetebezeichnung", "VIVIDT8-DEMO"),
        new("Hersteller", "Hersteller des Geraets", "GE Vingmed Ultrasound"),
        new("Modell", "Modellbezeichnung des Geraets", "Vivid T8"),
        new("Untersuchungsart", "Titel des Berichts bzw. Study Description", "Adult Echocardiography Procedure Report"),
        new("Ueberschrift", "Ueberschrift aus den GDT-Einstellungen inkl. Geraet", "Echokardiographie GE Vivid T8"),
        new("SopInstanceUid", "Eindeutige Kennung der Untersuchung", "1.2.826.0.1..."),
        new("StudyInstanceUid", "Eindeutige Kennung der Studie", "1.2.826.0.1..."),
        new("Empfaenger", "Empfaengerkennung aus den Einstellungen", "MEDOFF"),
        new("Sender", "Senderkennung aus den Einstellungen", "VIVIDT8"),
        new("Geraetekennung", "Geraete-/Verfahrenskennung (Feld 8402)", "SONO-ECHO"),
        new("TestId", "Test-Ident (Feld 8410)", "ECHO"),
        new("TestBezeichnung", "Testbezeichnung (Feld 8411)", "Echokardiographie"),
        new("GdtVersion", "GDT-Version aus den Einstellungen", "02.10"),
        new("Zeichensatz", "Zeichensatz-Code aus den Einstellungen", "3"),
        new("AnzahlMesswerte", "Anzahl der uebertragenen Messwerte", "61"),
        new("Heute", "Aktuelles Datum TT.MM.JJJJ", "20.08.2026"),
        new(MeasurementsPlaceholder, "Alle Messwerte, nach Region gruppiert (erzeugt mehrere Zeilen)", "[Left Ventricle] ..."),
        new(MeasurementsFlatPlaceholder, "Alle Messwerte ohne Gruppenueberschriften (erzeugt mehrere Zeilen)", "LVIDd: 4.21 cm")
    ];

    /// <summary>Erzeugt die GDT-Felder aus der Vorlage.</summary>
    public IReadOnlyList<GdtField> Build(GdtTemplate template, SrReport report)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(report);

        var values = BuildValues(report);
        var fields = new List<GdtField>();

        foreach (var line in template.Lines.Where(l => l.Enabled && IsValidFieldId(l.FieldId)))
        {
            var expansion = ExpandMultiLinePlaceholder(line, report);
            if (expansion is not null)
            {
                fields.AddRange(expansion.Select(text => new GdtField(line.FieldId, text)));
                continue;
            }

            var resolved = Resolve(line.Content, values);

            // Enthielt die Zeile nur Platzhalter ohne Wert, wird sie weggelassen -
            // sonst stuenden im Befund Zeilen wie "Accession: " ohne Inhalt.
            if (resolved.HadPlaceholder && !resolved.HadValue) continue;

            foreach (var wrapped in _composer.WrapLines([resolved.Text]))
                fields.Add(new GdtField(line.FieldId, wrapped));
        }

        return fields;
    }

    /// <summary>Vorschau fuer den Editor: der fertige Satz als Text.</summary>
    public string BuildPreview(GdtTemplate template, SrReport report, System.Text.Encoding encoding)
        => GdtLineFormatter.Format(Build(template, report), encoding);

    private IReadOnlyList<string>? ExpandMultiLinePlaceholder(GdtTemplateLine line, SrReport report)
    {
        var trimmed = line.Content.Trim();
        if (string.Equals(trimmed, "{" + MeasurementsPlaceholder + "}", StringComparison.OrdinalIgnoreCase))
            return _composer.BuildMeasurementLines(report, withGroups: true);

        if (string.Equals(trimmed, "{" + MeasurementsFlatPlaceholder + "}", StringComparison.OrdinalIgnoreCase))
            return _composer.BuildMeasurementLines(report, withGroups: false);

        return null;
    }

    private Dictionary<string, string> BuildValues(SrReport report)
    {
        var h = report.Header;
        var title = string.Join(" ", new[] { _settings.ReportTitle, h.DeviceDescription }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PatientNummer"] = h.PatientId,
            ["Nachname"] = h.LastName,
            ["Vorname"] = h.FirstName,
            ["Geburtsdatum"] = DicomValueConverter.ToGdtDate(h.PatientBirthDate),
            ["GeburtsdatumLang"] = DicomValueConverter.ToDisplayDate(h.PatientBirthDate),
            ["Geschlecht"] = DicomValueConverter.ToGdtSex(h.PatientSex),
            ["Untersuchungsdatum"] = DicomValueConverter.ToGdtDate(h.StudyDate),
            ["Untersuchungszeit"] = DicomValueConverter.ToGdtTime(h.StudyTime),
            ["DatumLang"] = DicomValueConverter.ToDisplayDate(h.StudyDate),
            ["ZeitLang"] = DicomValueConverter.ToDisplayTime(h.StudyTime),
            ["Anforderungsnummer"] = h.AccessionNumber,
            ["Geraet"] = FirstNonEmpty(h.StationName, h.ManufacturerModelName),
            ["Hersteller"] = h.Manufacturer,
            ["Modell"] = h.ManufacturerModelName,
            ["Untersuchungsart"] = FirstNonEmpty(h.StudyDescription, h.DocumentTitle),
            ["Ueberschrift"] = title,
            ["SopInstanceUid"] = h.SopInstanceUid,
            ["StudyInstanceUid"] = h.StudyInstanceUid,
            ["Empfaenger"] = _settings.ReceiverId,
            ["Sender"] = _settings.SenderId,
            ["Geraetekennung"] = _settings.TestType,
            ["TestId"] = _settings.TestId,
            ["TestBezeichnung"] = _settings.TestName,
            ["GdtVersion"] = _settings.Version,
            ["Zeichensatz"] = _settings.Charset,
            ["AnzahlMesswerte"] = report.Measurements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Heute"] = DateTime.Now.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Ersetzt die Platzhalter und meldet, ob ueberhaupt ein Wert geliefert wurde.</summary>
    private static (string Text, bool HadPlaceholder, bool HadValue) Resolve(
        string content, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(content)) return (string.Empty, false, false);

        var result = content;
        var hadPlaceholder = false;
        var hadValue = false;

        foreach (var (key, value) in values)
        {
            var token = "{" + key + "}";
            if (result.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;

            hadPlaceholder = true;
            if (!string.IsNullOrWhiteSpace(value)) hadValue = true;
            result = ReplaceToken(result, key, value);
        }

        return (result.Trim(), hadPlaceholder, hadValue);
    }

    private static string ReplaceToken(string text, string name, string value)
    {
        var token = "{" + name + "}";
        var index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            text = text[..index] + value + text[(index + token.Length)..];
            index = text.IndexOf(token, index + value.Length, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    public static bool IsValidFieldId(string fieldId)
        => fieldId is { Length: 4 } && fieldId.All(char.IsDigit);

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
