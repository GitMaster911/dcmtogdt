using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Processing;
using DCMtoGDTReports.Core.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DCMtoGDTReports.Tests;

/// <summary>
/// Prueft die Weiterleitung an das PVS. Der Ablauf muss auch dann funktionieren, wenn
/// keine GDT-Datei erzeugt werden konnte - sonst blieben DICOM-Daten im Eingang haengen.
/// </summary>
public class DicomForwarderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dcm2gdt-fwd-{Guid.NewGuid():N}");
    private readonly string _source;
    private readonly string _target;

    public DicomForwarderTests()
    {
        _source = Path.Combine(_root, "in");
        _target = Path.Combine(_root, "pvs");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_target);
    }

    private string CreateFile(string name, string content = "DICOM")
    {
        var path = Path.Combine(_source, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static DicomForwarder Forwarder(string target) => new(target, NullLogger.Instance);

    [Fact]
    public void OhneZielordner_WirdNichtsVerschoben()
    {
        var file = CreateFile("SRc.1.2.3.dcm");

        Assert.False(Forwarder(string.Empty).Forward(file));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Datei_WirdVerschobenUndImEingangEntfernt()
    {
        var file = CreateFile("SRc.1.2.3.dcm");

        Assert.True(Forwarder(_target).Forward(file));

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(_target, "SRc.1.2.3.dcm")));
    }

    [Fact]
    public void Dateiname_BleibtErhalten()
    {
        var file = CreateFile("US.9.8.7.dcm");

        Forwarder(_target).Forward(file);

        Assert.Equal("US.9.8.7.dcm", Path.GetFileName(Directory.EnumerateFiles(_target).Single()));
    }

    [Fact]
    public void Inhalt_BleibtUnveraendert()
    {
        var file = CreateFile("SRc.1.dcm", "unveraenderter Inhalt");

        Forwarder(_target).Forward(file);

        Assert.Equal("unveraenderter Inhalt", File.ReadAllText(Path.Combine(_target, "SRc.1.dcm")));
    }

    [Fact]
    public void KeineTempDateiBleibtLiegen()
    {
        Forwarder(_target).Forward(CreateFile("SRc.1.dcm"));

        Assert.Empty(Directory.EnumerateFiles(_target, "*.tmp"));
    }

    [Fact]
    public void GleicherDateiname_WirdNichtUeberschrieben()
    {
        File.WriteAllText(Path.Combine(_target, "SRc.1.dcm"), "bereits vorhanden");
        var file = CreateFile("SRc.1.dcm", "neu");

        Forwarder(_target).Forward(file);

        Assert.Equal("bereits vorhanden", File.ReadAllText(Path.Combine(_target, "SRc.1.dcm")));
        Assert.Equal("neu", File.ReadAllText(Path.Combine(_target, "SRc.1_1.dcm")));
    }

    [Fact]
    public void FehlendeQuelle_LiefertFalseStattAusnahme()
    {
        Assert.False(Forwarder(_target).Forward(Path.Combine(_source, "gibtesnicht.dcm")));
    }

    [Fact]
    public void Zielordner_WirdBeiBedarfAngelegt()
    {
        var target = Path.Combine(_root, "neu", "import");
        var file = CreateFile("SRc.1.dcm");

        Assert.True(Forwarder(target).Forward(file));
        Assert.True(File.Exists(Path.Combine(target, "SRc.1.dcm")));
    }

    [Fact]
    public async Task Verarbeitung_ReichtDieDateiAuchOhneMesswerteWeiter()
    {
        var settings = new AppSettings
        {
            InputFolder = _source,
            OutputFolder = Path.Combine(_root, "gdt"),
            ArchiveFolder = Path.Combine(_root, "archiv"),
            ErrorFolder = Path.Combine(_root, "fehler"),
            RegistryDatabasePath = Path.Combine(_root, "p.db"),
            MeasurementCatalogPath = Path.Combine(_root, "c.json")
        };
        settings.Processing.ForwardFolder = _target;

        var registry = new SqliteProcessedFileRegistry(settings.RegistryDatabasePath);
        registry.Initialize();

        // Keine gueltige DICOM-Datei: die Auswertung scheitert, das PVS muss sie trotzdem bekommen.
        var file = CreateFile("SRc.kaputt.dcm", "kein DICOM");

        var result = await new SrFileProcessor(settings, registry).ProcessAsync(file);

        Assert.Equal(Core.Models.ProcessingStatus.Failed, result.Status);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(_target, "SRc.kaputt.dcm")));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
