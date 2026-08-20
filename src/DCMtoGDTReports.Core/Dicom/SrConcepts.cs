namespace DCMtoGDTReports.Core.Dicom;

/// <summary>
/// Concept-Name-Codes, die im Adult Echocardiography Procedure Report (TID 5200) des GE Vivid T8
/// als Modifikatoren an den NUM-Knoten haengen. Ermittelt aus der realen SR-Beispieldatei.
/// </summary>
public static class SrConcepts
{
    public const string FindingSite = "G-C0E3";           // SRT
    public const string MeasurementMethod = "G-C036";     // SRT
    public const string ImageMode = "G-0373";             // SRT
    public const string CardiacCyclePoint = "R-4089A";    // SRT
    public const string DirectionOfFlow = "G-C048";       // SRT
    public const string Derivation = "121401";            // DCM
    public const string SelectionStatus = "121404";       // DCM
    public const string MeasurementGroup = "125007";      // DCM

    public const string RelationshipHasConceptMod = "HAS CONCEPT MOD";
    public const string RelationshipHasProperties = "HAS PROPERTIES";
    public const string RelationshipHasAcqContext = "HAS ACQ CONTEXT";
    public const string RelationshipContains = "CONTAINS";

    public const string ValueTypeContainer = "CONTAINER";
    public const string ValueTypeNum = "NUM";
    public const string ValueTypeCode = "CODE";
}

/// <summary>Ein aus einer Code-Sequenz gelesener Code.</summary>
public readonly record struct SrCode(string Value, string Scheme, string Meaning)
{
    public static readonly SrCode Empty = new(string.Empty, string.Empty, string.Empty);

    public bool IsEmpty => string.IsNullOrEmpty(Value) && string.IsNullOrEmpty(Meaning);

    /// <summary>Kanonische Form "SCHEMA:CODE", z. B. "LN:29436-3".</summary>
    public string Qualified => string.IsNullOrEmpty(Scheme) ? Value : $"{Scheme}:{Value}";

    public bool Matches(string codeValue) => string.Equals(Value, codeValue, StringComparison.OrdinalIgnoreCase);
}
