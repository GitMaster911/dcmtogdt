using DCMtoGDTReports.Core.Logging;
using Xunit;

namespace DCMtoGDTReports.Tests;

/// <summary>
/// Die Oberflaeche liest die Logdatei des Dienstes mit. Entscheidend ist, dass die Datei auch
/// dann gelesen werden kann, wenn der Dienst sie noch offen haelt, und dass keine Zeile doppelt
/// erscheint.
/// </summary>
public class ServiceLogReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dcm2gdt-log-{Guid.NewGuid():N}");

    public ServiceLogReaderTests() => Directory.CreateDirectory(_root);

    private string WriteLog(string name, params string[] lines)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void OhneLogordner_LiefertNichts()
        => Assert.Empty(new ServiceLogReader(Path.Combine(_root, "fehlt")).ReadNewLines());

    [Fact]
    public void LiestVorhandeneZeilen()
    {
        WriteLog("worker-20240115.log", "erste Zeile", "zweite Zeile");

        var lines = new ServiceLogReader(_root).ReadNewLines();

        Assert.Equal(["erste Zeile", "zweite Zeile"], lines);
    }

    [Fact]
    public void LiefertNurNeueZeilen()
    {
        var path = WriteLog("worker-20240115.log", "alt");
        var reader = new ServiceLogReader(_root);
        reader.ReadNewLines();

        File.AppendAllLines(path, ["neu"]);

        Assert.Equal(["neu"], reader.ReadNewLines());
    }

    [Fact]
    public void BeimErstenLesen_WirdBegrenzt()
    {
        WriteLog("worker-20240115.log", Enumerable.Range(1, 50).Select(i => $"Zeile {i}").ToArray());

        var lines = new ServiceLogReader(_root).ReadNewLines(initialLines: 10);

        Assert.Equal(10, lines.Count);
        Assert.Equal("Zeile 50", lines[^1]);
    }

    [Fact]
    public void GeoeffneteDatei_KannGelesenWerden()
    {
        var path = WriteLog("worker-20240115.log", "waehrend der Dienst schreibt");

        // So haelt Serilog die Datei offen.
        using var _ = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);

        Assert.Single(new ServiceLogReader(_root).ReadNewLines());
    }

    [Fact]
    public void NeueTagesdatei_WirdVonVorneGelesen()
    {
        WriteLog("worker-20240115.log", "gestern");
        var reader = new ServiceLogReader(_root);
        reader.ReadNewLines();

        var heute = WriteLog("worker-20240116.log", "heute");
        File.SetLastWriteTimeUtc(heute, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(["heute"], reader.ReadNewLines());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
