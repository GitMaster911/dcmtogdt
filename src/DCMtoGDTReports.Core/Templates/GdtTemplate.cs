namespace DCMtoGDTReports.Core.Templates;

/// <summary>
/// Eine Zeile der GDT-Vorlage. Der Inhalt darf Platzhalter wie {Nachname} enthalten,
/// die beim Erzeugen der Datei durch die Werte aus dem Structured Report ersetzt werden.
/// </summary>
public sealed class GdtTemplateLine
{
    /// <summary>Zeile wird ausgegeben. In der GUI die Ankreuzbox links.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Vierstellige GDT-Feldkennung, z. B. "3101".</summary>
    public string FieldId { get; set; } = string.Empty;

    /// <summary>Inhalt mit optionalen Platzhaltern, z. B. "{Nachname}".</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Klartextbeschreibung fuer die Anzeige in der GUI.</summary>
    public string Description { get; set; } = string.Empty;

    public GdtTemplateLine Clone() => (GdtTemplateLine)MemberwiseClone();
}

/// <summary>
/// Frei zusammenstellbarer Aufbau der GDT-Datei. Solange die Vorlage nicht aktiviert ist,
/// wird der fest eingebaute Standardaufbau der Satzart 6310 verwendet.
/// </summary>
public sealed class GdtTemplate
{
    /// <summary>Vorlage statt des eingebauten Standardaufbaus verwenden.</summary>
    public bool Enabled { get; set; }

    public List<GdtTemplateLine> Lines { get; set; } = [];

    /// <summary>
    /// Der eingebaute Standardaufbau als bearbeitbare Vorlage. Dient in der GUI als
    /// Ausgangspunkt und als Ziel der Schaltflaeche "Standard wiederherstellen".
    /// </summary>
    public static GdtTemplate CreateDefault() => new()
    {
        Enabled = false,
        Lines =
        [
            new() { FieldId = "8000", Content = "6310", Description = "Satzart: Ergebnisdaten an das PVS" },
            new() { FieldId = "8315", Content = "{Empfaenger}", Description = "Empfaengerkennung (MEDICAL OFFICE)" },
            new() { FieldId = "8316", Content = "{Sender}", Description = "Senderkennung (Messgeraet)" },
            new() { FieldId = "9206", Content = "{Zeichensatz}", Description = "Zeichensatz der Datei" },
            new() { FieldId = "9218", Content = "{GdtVersion}", Description = "GDT-Version" },
            new() { FieldId = "3000", Content = "{PatientNummer}", Description = "Patientennummer" },
            new() { FieldId = "3101", Content = "{Nachname}", Description = "Nachname" },
            new() { FieldId = "3102", Content = "{Vorname}", Description = "Vorname" },
            new() { FieldId = "3103", Content = "{Geburtsdatum}", Description = "Geburtsdatum TTMMJJJJ" },
            new() { FieldId = "3110", Content = "{Geschlecht}", Description = "Geschlecht (1 = m, 2 = w)" },
            new() { FieldId = "6200", Content = "{Untersuchungsdatum}", Description = "Untersuchungsdatum TTMMJJJJ" },
            new() { FieldId = "6201", Content = "{Untersuchungszeit}", Description = "Untersuchungszeit HHMMSS" },
            new() { FieldId = "8402", Content = "{Geraetekennung}", Description = "Geraete-/Verfahrenskennung" },
            new() { FieldId = "8410", Content = "{TestId}", Description = "Test-Ident" },
            new() { FieldId = "8411", Content = "{TestBezeichnung}", Description = "Testbezeichnung" },
            new() { FieldId = "6220", Content = "{Ueberschrift}", Description = "Ueberschrift des Befundtexts" },
            new() { FieldId = "6220", Content = "", Description = "Leerzeile" },
            new() { FieldId = "6220", Content = "Untersuchung:", Description = "Zwischenueberschrift" },
            new() { FieldId = "6220", Content = "Datum: {DatumLang}", Description = "Untersuchungsdatum TT.MM.JJJJ" },
            new() { FieldId = "6220", Content = "Uhrzeit: {ZeitLang}", Description = "Untersuchungszeit HH:MM:SS" },
            new() { FieldId = "6220", Content = "Accession: {Anforderungsnummer}", Description = "Anforderungsnummer" },
            new() { FieldId = "6220", Content = "Geraet: {Geraet}", Description = "Geraetebezeichnung" },
            new() { FieldId = "6220", Content = "", Description = "Leerzeile" },
            new() { FieldId = "6220", Content = "Messwerte:", Description = "Zwischenueberschrift" },
            new() { FieldId = "6220", Content = "{Messwerte}", Description = "Alle Messwerte, nach Region gruppiert" },
            new() { FieldId = "6227", Content = "Quelle: DICOM Structured Report", Description = "Kommentarzeile" },
            new() { FieldId = "6227", Content = "SOPInstanceUID: {SopInstanceUid}", Description = "Kennung der Untersuchung" }
        ]
    };

    public GdtTemplate Clone() => new()
    {
        Enabled = Enabled,
        Lines = Lines.Select(l => l.Clone()).ToList()
    };
}
