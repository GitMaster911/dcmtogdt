using System.Text.Json.Serialization;

namespace DCMtoGDTReports.Core.Configuration;

/// <summary>
/// Vollstaendige Anwendungskonfiguration. Alle Pfade sind konfigurierbar, es gibt keine harten Pfade im Code.
/// </summary>
public sealed class AppSettings
{
    public string InputFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public string ArchiveFolder { get; set; } = string.Empty;
    public string ErrorFolder { get; set; } = string.Empty;

    /// <summary>Optionaler DCMTK-Pfad. Leer = eingebautes DICOM-Toolkit verwenden.</summary>
    public string DcmtkPath { get; set; } = string.Empty;

    public GdtSettings Gdt { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();

    /// <summary>Filterung der Messwerte, bevor sie in die GDT-Datei geschrieben werden.</summary>
    public MeasurementFilterSettings MeasurementFilter { get; set; } = new();

    /// <summary>Automatische Aktualisierung des Tools.</summary>
    public UpdateSettings Update { get; set; } = new();

    /// <summary>Frei zusammenstellbarer Aufbau der GDT-Datei (Vorlagen-Editor in der GUI).</summary>
    public Templates.GdtTemplate GdtTemplate { get; set; } = Templates.GdtTemplate.CreateDefault();

    /// <summary>Ablageort der SQLite-Registry. Leer = Standardpfad neben der Konfigurationsdatei.</summary>
    public string RegistryDatabasePath { get; set; } = string.Empty;

    /// <summary>Ablageort des gelernten Messwert-Katalogs. Leer = catalog.json neben der Konfiguration.</summary>
    public string MeasurementCatalogPath { get; set; } = string.Empty;

    /// <summary>Ablageort der Logdateien. Leer = Unterordner "logs".</summary>
    public string LogFolder { get; set; } = string.Empty;

    /// <summary>Optionale Zuordnung von SR-Bezeichnungen auf Kurznamen (Concept-Code oder Code Meaning als Schluessel).</summary>
    public Dictionary<string, string> MeasurementShortNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optionale Abkuerzungen fuer Messmethoden, z. B. lange AFI-Bezeichnungen.</summary>
    public Dictionary<string, string> MethodShortNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GdtSettings
{
    /// <summary>Feld 8316 - Senderkennung (das Messgeraet bzw. dieses Tool).</summary>
    public string SenderId { get; set; } = "VIVIDT8";

    /// <summary>Feld 8315 - Empfaengerkennung (MEDICAL OFFICE).</summary>
    public string ReceiverId { get; set; } = "MEDOFF";

    /// <summary>Feld 9218 - GDT-Version.</summary>
    public string Version { get; set; } = "02.10";

    /// <summary>Feld 9206 - Zeichensatz. 1 = 7-Bit, 2 = IBM CP437, 3 = ISO8859-1/ANSI.</summary>
    public string Charset { get; set; } = "3";

    /// <summary>
    /// Feld 8402 - Geraete-/Verfahrenskennung. Daran erkennt das PVS, aus welchem Verfahren
    /// die Daten stammen. Uebliche Kennungen: EKG01, EKG02, ERGO01, LUFU01, SPIRO01, LZEKG01,
    /// LZRR01, SONO01, ECHO01. Der Wert muss zu dem passen, was in MEDICAL OFFICE bzw. im
    /// BITS GDT Mover fuer dieses Geraet hinterlegt ist.
    /// </summary>
    public string TestType { get; set; } = "ECHO01";

    /// <summary>Feld 8410 - Test-Ident.</summary>
    public string TestId { get; set; } = "ECHO";

    /// <summary>Feld 8411 - Testbezeichnung.</summary>
    public string TestName { get; set; } = "Echokardiographie";

    /// <summary>Codepage fuer die Ausgabedatei. 28591 = ISO-8859-1, 1252 = Windows-1252.</summary>
    public int EncodingCodePage { get; set; } = 28591;

    /// <summary>Ueberschrift der ersten Ergebniszeile.</summary>
    public string ReportTitle { get; set; } = "Echokardiographie";

    /// <summary>Maximale Zeichenzahl pro Ergebniszeile (Feld 6220). GDT erlaubt maximal 60 Zeichen.</summary>
    public int MaxResultLineLength { get; set; } = 60;

    /// <summary>Nachkommastellen fuer Messwerte.</summary>
    public int DecimalPlaces { get; set; } = 2;

    /// <summary>Dezimaltrennzeichen im Ergebnistext.</summary>
    public string DecimalSeparator { get; set; } = ".";

    /// <summary>Dateinamensmuster der GDT-Datei. Platzhalter: {sender} {receiver} {patientId} {timestamp} {accession}.</summary>
    public string FileNamePattern { get; set; } = "{sender}{receiver}_{patientId}_{timestamp}.gdt";

    /// <summary>Zusaetzlich je Messwert die Felder 8410/8411/8420/8421 ausgeben.</summary>
    public bool EmitStructuredTestValues { get; set; }

    /// <summary>Feld 8100 (Satzlaenge) mit ausgeben.</summary>
    public bool IncludeRecordLength { get; set; } = true;

    /// <summary>Kommentarzeilen fuer Feld 6227 (z. B. Quellenangabe).</summary>
    public bool IncludeSourceComment { get; set; } = true;
}

public sealed class ProcessingSettings
{    /// <summary>
    /// Wenn true, wird die Original-SR-Datei nach der Verarbeitung in den Archivordner verschoben.
    /// Standard false: die Originaldatei bleibt am Ursprungsort, es wird nur mit einer temporaeren Kopie gearbeitet.
    /// </summary>
    public bool MoveProcessedFiles { get; set; }

    /// <summary>Wenn true, wird zusaetzlich eine Kopie der Originaldatei ins Archiv gelegt.</summary>
    public bool CopyToArchive { get; set; } = true;

    /// <summary>dsr2xml-/Struktur-XML zur Fehlersuche im Archivordner behalten.</summary>
    public bool KeepXmlDebugFiles { get; set; } = true;

    /// <summary>Kriterium der Dublettenpruefung.</summary>
    public string PreventDuplicateBy { get; set; } = "SHA256_OR_SOPInstanceUID";

    /// <summary>Dateimuster im Eingangsordner.</summary>
    public string FilePattern { get; set; } = "SR*.dcm";

    /// <summary>Bevorzugte Auswertungs-Engine: "Builtin" (fo-dicom) oder "Dcmtk".</summary>
    public string PreferredEngine { get; set; } = "Builtin";

    /// <summary>Wartezeit in Millisekunden zwischen zwei Stabilitaetspruefungen einer neuen Datei.</summary>
    public int FileStabilityPollMilliseconds { get; set; } = 750;

    /// <summary>Maximale Anzahl Stabilitaetspruefungen, bevor die Datei als fehlerhaft gilt.</summary>
    public int FileStabilityMaxAttempts { get; set; } = 40;

    /// <summary>Intervall des zusaetzlichen Rescans in Sekunden (fuer Dateien, die der Watcher verpasst hat).</summary>
    public int RescanIntervalSeconds { get; set; } = 60;

    /// <summary>Bei Fehlern eine Kopie der Datei in den Fehlerordner legen.</summary>
    public bool CopyFailedFilesToErrorFolder { get; set; } = true;

    [JsonIgnore]
    public bool DuplicateBySha256 =>
        PreventDuplicateBy.Contains("SHA256", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool DuplicateBySopInstanceUid =>
        PreventDuplicateBy.Contains("SOPInstanceUID", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Umgang mit mehrfach gemessenen Werten (der Vivid T8 liefert z. B. EF je Herzschlag einzeln).
/// </summary>
public enum RepeatedValueMode
{
    /// <summary>Alle Einzelwerte ausgeben.</summary>
    All,

    /// <summary>Nur den ersten Wert je Messgroesse.</summary>
    First,

    /// <summary>Nur den letzten Wert je Messgroesse.</summary>
    Last,

    /// <summary>Arithmetisches Mittel aller Einzelwerte, mit Hinweis auf die Anzahl.</summary>
    Mean,

    /// <summary>Kleinster Einzelwert, mit Hinweis auf die Anzahl.</summary>
    Min,

    /// <summary>Groesster Einzelwert, mit Hinweis auf die Anzahl.</summary>
    Max,

    /// <summary>Eine Zeile mit Mittelwert sowie Minimum, Maximum und Anzahl der Messungen.</summary>
    MinMaxMean
}

/// <summary>
/// Konfigurierbarer Filter fuer die Messwerte. Ohne Aktivierung wird nichts verworfen -
/// es gehen also niemals unbemerkt klinische Werte verloren.
///
/// Alle Musterlisten unterstuetzen die Platzhalter * und ? und werden ohne
/// Beachtung der Gross-/Kleinschreibung ausgewertet. Ausschlusslisten haben Vorrang
/// vor Einschlusslisten. Eine leere Einschlussliste bedeutet "alles zulassen".
/// </summary>
public sealed class MeasurementFilterSettings
{
    /// <summary>
    /// Filter aktiv. Standard true mit <see cref="RepeatedValueMode.MinMaxMean"/>: der Vivid T8
    /// liefert dieselbe Messgroesse je Herzschlag einzeln, was den Befund unlesbar lang macht.
    /// Es geht dabei kein Wert verloren - Minimum, Maximum und Anzahl stehen in der Zeile.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nur Messwerte, fuer die eine Kurzbezeichnung gemappt ist.</summary>
    public bool OnlyMappedMeasurements { get; set; }

    /// <summary>Nur Messwerte, die das Geraet mit einem Selection Status als gewaehlt markiert hat.</summary>
    public bool OnlySelectedValues { get; set; }

    /// <summary>Muster fuer Concept-Code, Originalname oder Kurzname, z. B. "LVIDd" oder "LN:29436-3".</summary>
    public List<string> IncludeConcepts { get; set; } = [];

    public List<string> ExcludeConcepts { get; set; } = [];

    /// <summary>Muster fuer die Finding Site, z. B. "Left Ventricle" oder "*segment".</summary>
    public List<string> IncludeFindingSites { get; set; } = [];

    public List<string> ExcludeFindingSites { get; set; } = [];

    /// <summary>Muster fuer den Image Mode, z. B. "2D mode" oder "Doppler*".</summary>
    public List<string> IncludeImageModes { get; set; } = [];

    public List<string> ExcludeImageModes { get; set; } = [];

    /// <summary>Umgang mit Wiederholungsmessungen derselben Messgroesse.</summary>
    public RepeatedValueMode RepeatedValues { get; set; } = RepeatedValueMode.MinMaxMean;

    /// <summary>Obergrenze der Messwerte im Ergebnistext. 0 = unbegrenzt.</summary>
    public int MaxMeasurements { get; set; }
}

/// <summary>
/// Konfiguration der Selbstaktualisierung. Damit koennen mehrere Arbeitsplaetze zentral
/// auf denselben Stand gebracht werden, ohne dass jemand manuell Dateien kopiert.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>Updatepruefung aktiv.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Speicherort der Datei update.json. Erlaubt sind eine HTTPS-Adresse oder ein
    /// lokaler bzw. UNC-Pfad, z. B. \\server\software\DCMtoGDTReports\update.json.
    /// </summary>
    public string ManifestUrl { get; set; } = string.Empty;

    /// <summary>Beim Programmstart automatisch nach Updates suchen.</summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>Abstand der wiederkehrenden Pruefung in Stunden. 0 = nur beim Start.</summary>
    public int CheckIntervalHours { get; set; } = 24;

    /// <summary>
    /// Gefundene Updates ohne Rueckfrage installieren. Standard false: der Benutzer
    /// entscheidet, wann das Programm neu gestartet wird.
    /// </summary>
    public bool InstallAutomatically { get; set; }

    /// <summary>
    /// Name des Windows-Dienstes, der fuer die Aktualisierung gestoppt und danach wieder
    /// gestartet wird. Leer lassen, wenn kein Dienst installiert ist.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;
}
