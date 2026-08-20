using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DCMtoGDTReports.Core.Registry;

/// <summary>
/// SQLite-basierte Registry der bereits verarbeiteten SR-Dateien.
/// Verhindert, dass fuer dieselbe Untersuchung mehrfach eine GDT-Datei erzeugt wird.
/// </summary>
public sealed class SqliteProcessedFileRegistry : IProcessedFileRegistry
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public SqliteProcessedFileRegistry(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Pfad zur Registry-Datenbank fehlt.", nameof(databasePath));

        DatabasePath = databasePath;
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath { get; }

    public void Initialize()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ProcessedFiles (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath          TEXT NOT NULL,
                    FileName          TEXT NOT NULL,
                    FileSize          INTEGER NOT NULL,
                    LastWriteTimeUtc  TEXT NOT NULL,
                    Sha256            TEXT NOT NULL,
                    SopInstanceUid    TEXT NOT NULL,
                    StudyInstanceUid  TEXT NOT NULL,
                    AccessionNumber   TEXT NOT NULL,
                    PatientId         TEXT NOT NULL,
                    CreatedGdtFile    TEXT NOT NULL,
                    ProcessedAtUtc    TEXT NOT NULL,
                    Status            TEXT NOT NULL,
                    ErrorMessage      TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ProcessedFiles_Sha256 ON ProcessedFiles (Sha256);
                CREATE INDEX IF NOT EXISTS IX_ProcessedFiles_Sop    ON ProcessedFiles (SopInstanceUid);
                CREATE INDEX IF NOT EXISTS IX_ProcessedFiles_Status ON ProcessedFiles (Status);
                """;
            command.ExecuteNonQuery();
        }
    }

    public ProcessedFileEntry? FindSuccessful(string? sha256, string? sopInstanceUid)
    {
        var hasHash = !string.IsNullOrWhiteSpace(sha256);
        var hasUid = !string.IsNullOrWhiteSpace(sopInstanceUid);
        if (!hasHash && !hasUid) return null;

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {ColumnList}
                FROM ProcessedFiles
                WHERE Status IN ($status, $skipped)
                  AND (($hasHash = 1 AND Sha256 = $sha256)
                    OR ($hasUid  = 1 AND SopInstanceUid = $sop))
                ORDER BY Id DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$status", ProcessedFileEntry.StatusSuccess);
            command.Parameters.AddWithValue("$skipped", ProcessedFileEntry.StatusSkipped);
            command.Parameters.AddWithValue("$hasHash", hasHash ? 1 : 0);
            command.Parameters.AddWithValue("$hasUid", hasUid ? 1 : 0);
            command.Parameters.AddWithValue("$sha256", sha256 ?? string.Empty);
            command.Parameters.AddWithValue("$sop", sopInstanceUid ?? string.Empty);

            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }
    }

    public void Add(ProcessedFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ProcessedFiles
                    (FilePath, FileName, FileSize, LastWriteTimeUtc, Sha256, SopInstanceUid, StudyInstanceUid,
                     AccessionNumber, PatientId, CreatedGdtFile, ProcessedAtUtc, Status, ErrorMessage)
                VALUES
                    ($filePath, $fileName, $fileSize, $lastWrite, $sha256, $sop, $study,
                     $accession, $patientId, $gdt, $processedAt, $status, $error);
                """;
            command.Parameters.AddWithValue("$filePath", entry.FilePath);
            command.Parameters.AddWithValue("$fileName", entry.FileName);
            command.Parameters.AddWithValue("$fileSize", entry.FileSize);
            command.Parameters.AddWithValue("$lastWrite", ToIso(entry.LastWriteTimeUtc));
            command.Parameters.AddWithValue("$sha256", entry.Sha256);
            command.Parameters.AddWithValue("$sop", entry.SopInstanceUid);
            command.Parameters.AddWithValue("$study", entry.StudyInstanceUid);
            command.Parameters.AddWithValue("$accession", entry.AccessionNumber);
            command.Parameters.AddWithValue("$patientId", entry.PatientId);
            command.Parameters.AddWithValue("$gdt", entry.CreatedGdtFile);
            command.Parameters.AddWithValue("$processedAt", ToIso(entry.ProcessedAtUtc));
            command.Parameters.AddWithValue("$status", entry.Status);
            command.Parameters.AddWithValue("$error", entry.ErrorMessage);

            command.ExecuteNonQuery();

            using var idCommand = connection.CreateCommand();
            idCommand.CommandText = "SELECT last_insert_rowid();";
            entry.Id = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public IReadOnlyList<ProcessedFileEntry> GetRecent(int count)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {ColumnList} FROM ProcessedFiles ORDER BY Id DESC LIMIT $count;";
            command.Parameters.AddWithValue("$count", Math.Clamp(count, 1, 5000));

            using var reader = command.ExecuteReader();
            var results = new List<ProcessedFileEntry>();
            while (reader.Read()) results.Add(Map(reader));
            return results;
        }
    }

    public int CountByStatus(string status)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ProcessedFiles WHERE Status = $status;";
            command.Parameters.AddWithValue("$status", status);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public int Forget(string? sha256, string? sopInstanceUid)
    {
        var hasHash = !string.IsNullOrWhiteSpace(sha256);
        var hasUid = !string.IsNullOrWhiteSpace(sopInstanceUid);
        if (!hasHash && !hasUid) return 0;

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM ProcessedFiles
                WHERE ($hasHash = 1 AND Sha256 = $sha256)
                   OR ($hasUid  = 1 AND SopInstanceUid = $sop);
                """;
            command.Parameters.AddWithValue("$hasHash", hasHash ? 1 : 0);
            command.Parameters.AddWithValue("$hasUid", hasUid ? 1 : 0);
            command.Parameters.AddWithValue("$sha256", sha256 ?? string.Empty);
            command.Parameters.AddWithValue("$sop", sopInstanceUid ?? string.Empty);
            return command.ExecuteNonQuery();
        }
    }

    public int ForgetAll()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ProcessedFiles;";
            return command.ExecuteNonQuery();
        }
    }

    private const string ColumnList =
        "Id, FilePath, FileName, FileSize, LastWriteTimeUtc, Sha256, SopInstanceUid, StudyInstanceUid, " +
        "AccessionNumber, PatientId, CreatedGdtFile, ProcessedAtUtc, Status, ErrorMessage";

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static ProcessedFileEntry Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        FilePath = reader.GetString(1),
        FileName = reader.GetString(2),
        FileSize = reader.GetInt64(3),
        LastWriteTimeUtc = FromIso(reader.GetString(4)),
        Sha256 = reader.GetString(5),
        SopInstanceUid = reader.GetString(6),
        StudyInstanceUid = reader.GetString(7),
        AccessionNumber = reader.GetString(8),
        PatientId = reader.GetString(9),
        CreatedGdtFile = reader.GetString(10),
        ProcessedAtUtc = FromIso(reader.GetString(11)),
        Status = reader.GetString(12),
        ErrorMessage = reader.GetString(13)
    };

    private static string ToIso(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime FromIso(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.MinValue;
}
