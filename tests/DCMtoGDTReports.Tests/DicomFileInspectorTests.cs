using DCMtoGDTReports.Core.Dicom;
using FellowOakDicom;
using Xunit;

namespace DCMtoGDTReports.Tests;

/// <summary>
/// Die Erkennung eines Structured Report darf nicht am Dateinamen haengen: welche Namen im
/// Eingangsordner ankommen, bestimmt der DICOM-Speicherdienst und nicht das Ultraschallgeraet.
/// </summary>
public class DicomFileInspectorTests : IDisposable
{
    private const string ComprehensiveSr = "1.2.840.10008.5.1.4.1.1.88.33";
    private const string EnhancedSr = "1.2.840.10008.5.1.4.1.1.88.22";
    private const string UltrasoundImage = "1.2.840.10008.5.1.4.1.1.6.1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dcm2gdt-inspect-{Guid.NewGuid():N}");

    public DicomFileInspectorTests() => Directory.CreateDirectory(_root);

    private string CreateDicom(string fileName, string sopClassUid, string modality)
    {
        var path = Path.Combine(_root, fileName);
        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, sopClassUid },
            { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID().UID },
            { DicomTag.Modality, modality },
            { DicomTag.PatientID, "12345" }
        };

        new DicomFile(dataset).Save(path);
        return path;
    }

    [Theory]
    [InlineData("SRc.1.2.840.113619.2.400.1.dcm")]
    [InlineData("SR.1.2.840.113619.2.400.1")]
    [InlineData("1.2.840.113619.2.400.1")]
    [InlineData("beliebiger.name")]
    public void StructuredReport_WirdUnabhaengigVomDateinamenErkannt(string fileName)
    {
        var path = CreateDicom(fileName, ComprehensiveSr, "SR");

        Assert.True(DicomFileInspector.IsStructuredReport(path));
    }

    [Fact]
    public void EnhancedSr_WirdErkannt()
    {
        var path = CreateDicom("egal.dcm", EnhancedSr, "SR");

        Assert.True(DicomFileInspector.IsStructuredReport(path));
    }

    [Fact]
    public void Bild_WirdNichtAlsBerichtErkannt()
    {
        var path = CreateDicom("SRc.bild.dcm", UltrasoundImage, "US");

        Assert.False(DicomFileInspector.IsStructuredReport(path));
    }

    [Fact]
    public void KeineDicomDatei_LiefertFalse()
    {
        var path = Path.Combine(_root, "notiz.dcm");
        File.WriteAllText(path, "kein DICOM");

        Assert.False(DicomFileInspector.IsStructuredReport(path));
    }

    [Fact]
    public void FehlendeDatei_LiefertFalse()
        => Assert.False(DicomFileInspector.IsStructuredReport(Path.Combine(_root, "gibtesnicht.dcm")));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
