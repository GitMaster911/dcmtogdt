namespace DCMtoGDTReports.Core.Models;

public enum ProcessingStatus
{
    /// <summary>Datei wurde neu verarbeitet und eine GDT-Datei erzeugt.</summary>
    Processed,

    /// <summary>Datei war bereits erfolgreich verarbeitet (SHA256 oder SOPInstanceUID bekannt).</summary>
    AlreadyProcessed,

    /// <summary>Datei wurde bewusst uebersprungen (z. B. kein Messwert enthalten).</summary>
    Skipped,

    /// <summary>Verarbeitung fehlgeschlagen.</summary>
    Failed
}

/// <summary>
/// Ergebnis der Verarbeitung genau einer SR-Datei.
/// </summary>
public sealed class ProcessingResult
{
    public required string SourceFilePath { get; init; }
    public required ProcessingStatus Status { get; init; }
    public string? GdtFilePath { get; init; }
    public string? ErrorMessage { get; init; }
    public SrReport? Report { get; init; }
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.Now;
    public string? Sha256 { get; init; }

    public string StatusText => Status switch
    {
        ProcessingStatus.Processed => "Neu verarbeitet",
        ProcessingStatus.AlreadyProcessed => "Bereits verarbeitet",
        ProcessingStatus.Skipped => "Uebersprungen",
        ProcessingStatus.Failed => "Fehler",
        _ => Status.ToString()
    };

    public static ProcessingResult Success(string source, string gdtPath, SrReport report, string sha256) => new()
    {
        SourceFilePath = source,
        Status = ProcessingStatus.Processed,
        GdtFilePath = gdtPath,
        Report = report,
        Sha256 = sha256
    };

    public static ProcessingResult Duplicate(string source, string? existingGdt, string sha256) => new()
    {
        SourceFilePath = source,
        Status = ProcessingStatus.AlreadyProcessed,
        GdtFilePath = existingGdt,
        Sha256 = sha256
    };

    public static ProcessingResult Skip(string source, string reason, string? sha256 = null) => new()
    {
        SourceFilePath = source,
        Status = ProcessingStatus.Skipped,
        ErrorMessage = reason,
        Sha256 = sha256
    };

    public static ProcessingResult Failure(string source, string error, string? sha256 = null) => new()
    {
        SourceFilePath = source,
        Status = ProcessingStatus.Failed,
        ErrorMessage = error,
        Sha256 = sha256
    };
}
