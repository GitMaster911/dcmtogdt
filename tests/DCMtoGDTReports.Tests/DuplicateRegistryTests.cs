using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Core.Processing;
using DCMtoGDTReports.Core.Registry;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class DuplicateRegistryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"dcm2gdt-test-{Guid.NewGuid():N}.db");
    private readonly SqliteProcessedFileRegistry _registry;

    public DuplicateRegistryTests()
    {
        _registry = new SqliteProcessedFileRegistry(_databasePath);
        _registry.Initialize();
    }

    [Fact]
    public void FindSuccessful_LeereRegistry_LiefertNull()
    {
        Assert.Null(_registry.FindSuccessful("ABC", "1.2.3"));
    }

    [Fact]
    public void FindSuccessful_ErkenntDubletteUeberSha256()
    {
        _registry.Add(CreateEntry("HASH1", "1.2.3", ProcessedFileEntry.StatusSuccess));

        var found = _registry.FindSuccessful("HASH1", null);

        Assert.NotNull(found);
        Assert.Equal("export.gdt", found.CreatedGdtFile);
    }

    [Fact]
    public void FindSuccessful_ErkenntDubletteUeberSopInstanceUid()
    {
        _registry.Add(CreateEntry("HASH1", "1.2.3", ProcessedFileEntry.StatusSuccess));

        Assert.NotNull(_registry.FindSuccessful(null, "1.2.3"));
    }

    [Fact]
    public void FindSuccessful_IgnoriertFehlgeschlageneVerarbeitung()
    {
        _registry.Add(CreateEntry("HASH2", "9.9.9", ProcessedFileEntry.StatusFailed));

        Assert.Null(_registry.FindSuccessful("HASH2", "9.9.9"));
    }

    [Fact]
    public void FindSuccessful_UebersprungeneDateienWerdenNichtErneutVerarbeitet()
    {
        _registry.Add(CreateEntry("HASH3", "8.8.8", ProcessedFileEntry.StatusSkipped));

        Assert.NotNull(_registry.FindSuccessful("HASH3", null));
    }

    [Fact]
    public void GetRecent_LiefertNeuesteZuerst()
    {
        _registry.Add(CreateEntry("A", "1", ProcessedFileEntry.StatusSuccess));
        _registry.Add(CreateEntry("B", "2", ProcessedFileEntry.StatusSuccess));

        var recent = _registry.GetRecent(10);

        Assert.Equal("B", recent[0].Sha256);
        Assert.Equal(2, _registry.CountByStatus(ProcessedFileEntry.StatusSuccess));
    }

    [Fact]
    public async Task ComputeSha256_GleicherInhaltErgibtGleichenHash()
    {
        var first = Path.GetTempFileName();
        var second = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(first, "DICOM-Testinhalt");
            await File.WriteAllTextAsync(second, "DICOM-Testinhalt");

            var hashA = await FileHasher.ComputeSha256Async(first);
            var hashB = await FileHasher.ComputeSha256Async(second);

            Assert.Equal(hashA, hashB);
            Assert.Equal(64, hashA.Length);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    private static ProcessedFileEntry CreateEntry(string sha, string sop, string status) => new()
    {
        FilePath = @"C:\in\SR1.dcm",
        FileName = "SR1.dcm",
        FileSize = 1234,
        LastWriteTimeUtc = DateTime.UtcNow,
        Sha256 = sha,
        SopInstanceUid = sop,
        StudyInstanceUid = "1.2.0",
        AccessionNumber = "1000042",
        PatientId = "12345",
        CreatedGdtFile = "export.gdt",
        Status = status,
        ErrorMessage = string.Empty
    };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }
}
