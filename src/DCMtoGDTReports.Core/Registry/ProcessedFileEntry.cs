namespace DCMtoGDTReports.Core.Registry;

/// <summary>
/// Ein Eintrag in der Dubletten-Registry. Wird pro verarbeiteter SR-Datei gespeichert.
/// </summary>
public sealed class ProcessedFileEntry
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string SopInstanceUid { get; set; } = string.Empty;
    public string StudyInstanceUid { get; set; } = string.Empty;
    public string AccessionNumber { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string CreatedGdtFile { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>"Success" oder "Failed".</summary>
    public string Status { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public const string StatusSuccess = "Success";
    public const string StatusFailed = "Failed";

    /// <summary>Datei war auswertbar, enthielt aber keine verwertbaren Messwerte.</summary>
    public const string StatusSkipped = "Skipped";
}

public interface IProcessedFileRegistry
{
    /// <summary>Legt das Schema an, falls noch nicht vorhanden.</summary>
    void Initialize();

    /// <summary>
    /// Sucht einen bereits abgeschlossenen Eintrag (Success oder Skipped) zu Hash bzw. SOPInstanceUID.
    /// Null bedeutet: die Datei darf verarbeitet werden.
    /// </summary>
    ProcessedFileEntry? FindSuccessful(string? sha256, string? sopInstanceUid);

    void Add(ProcessedFileEntry entry);

    IReadOnlyList<ProcessedFileEntry> GetRecent(int count);

    int CountByStatus(string status);
}
