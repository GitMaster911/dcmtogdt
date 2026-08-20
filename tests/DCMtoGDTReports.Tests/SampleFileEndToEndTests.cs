using System.Text;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Mapping;
using Xunit;

namespace DCMtoGDTReports.Tests;

/// <summary>
/// End-to-End-Test mit der echten SR-Beispieldatei des GE Vivid T8:
/// SR*.dcm -> Auswertung -> Mapping -> GDT-Datei.
/// </summary>
public class SampleFileEndToEndTests
{
    private static string? FindSampleFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var samples = Path.Combine(directory.FullName, "samples");
            if (Directory.Exists(samples))
            {
                var file = Directory.EnumerateFiles(samples, "SR*.dcm").FirstOrDefault();
                if (file is not null) return file;
            }
            directory = directory.Parent;
        }
        return null;
    }

    [Fact]
    public async Task BeispielDatei_WirdVollstaendigAusgewertet()
    {
        var sample = FindSampleFile();
        if (sample is null) return; // Ohne Beispieldatei laesst sich der Test nicht ausfuehren.

        var report = await new FoDicomSrExtractor().ExtractAsync(sample);

        Assert.Equal("SR", report.Header.Modality);
        Assert.Equal("GE Vingmed Ultrasound", report.Header.Manufacturer);
        Assert.Equal("Adult Echocardiography Procedure Report", report.Header.DocumentTitle);
        Assert.False(string.IsNullOrWhiteSpace(report.Header.SopInstanceUid));
        Assert.NotEmpty(report.Measurements);

        // Der Vivid T8 liefert jeden Messwert doppelt - nach der Bereinigung muss es weniger sein.
        Assert.True(report.Measurements.Count < report.RawMeasurementCount);

        // Alle Messwerte muessen einen Namen und einen Rohwert besitzen.
        Assert.All(report.Measurements, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.False(string.IsNullOrWhiteSpace(m.RawValue));
        });
    }

    [Fact]
    public async Task BeispielDatei_ErzeugtGueltigeGdtDatei()
    {
        var sample = FindSampleFile();
        if (sample is null) return; // Ohne Beispieldatei laesst sich der Test nicht ausfuehren.

        var settings = new GdtSettings { SenderId = "VIVIDT8", ReceiverId = "MEDOFF" };
        var report = await new FoDicomSrExtractor().ExtractAsync(sample);
        new MeasurementMapper().Apply(report.Measurements, settings);

        var outputFolder = Path.Combine(Path.GetTempPath(), $"dcm2gdt-{Guid.NewGuid():N}");
        try
        {
            var writer = new GdtFileWriter(settings);
            var path = writer.Write(report, outputFolder);

            var bytes = File.ReadAllBytes(path);
            var text = writer.Encoding.GetString(bytes);
            var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal("01380006310", lines[0]);
            Assert.Equal(bytes.Length, int.Parse(lines.Single(l => l[3..7] == "8100")[7..]));

            // Jede Zeile muss die GDT-Laengenregel erfuellen: Inhaltslaenge + 9.
            foreach (var line in lines)
                Assert.Equal(writer.Encoding.GetByteCount(line[7..]) + 9, int.Parse(line[..3]));

            // Der Ergebnistext muss die gemappten Kurznamen enthalten.
            var resultText = string.Join(Environment.NewLine, lines.Where(l => l[3..7] == "6220").Select(l => l[7..]));
            Assert.Contains("LVIDd", resultText, StringComparison.Ordinal);
            Assert.Contains("Messwerte:", resultText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputFolder)) Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public void MeasurementMapper_OhneTrefferBleibtOriginalname()
    {
        var mapper = new MeasurementMapper();
        var unknown = new Core.Models.MeasurementResult { Name = "Unbekannter GE-Messwert", SourceCode = "99GEMS:X-1" };

        Assert.Equal("Unbekannter GE-Messwert", mapper.ResolveShortName(unknown));
    }

    [Fact]
    public void MeasurementMapper_BenutzerMappingUeberschreibtStandard()
    {
        var mapper = new MeasurementMapper(new Dictionary<string, string>
        {
            ["Left Ventricle Internal End Diastolic Dimension"] = "LVEDD"
        });

        var measurement = new Core.Models.MeasurementResult
        {
            Name = "Left Ventricle Internal End Diastolic Dimension",
            SourceCode = "LN:29436-3"
        };

        Assert.Equal("LVEDD", mapper.ResolveShortName(measurement));
    }

    [Fact]
    public void GdtFileWriter_VerwendetKonfigurierteKodierung()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Assert.Equal(28591, new GdtFileWriter(new GdtSettings { EncodingCodePage = 28591 }).Encoding.CodePage);
        Assert.Equal(1252, new GdtFileWriter(new GdtSettings { EncodingCodePage = 1252 }).Encoding.CodePage);
    }
}
