using System.Text;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Gdt;
using Xunit;

namespace DCMtoGDTReports.Tests;

/// <summary>
/// Haelt die mitgelieferte Beispielausgabe aktuell. Der Test schreibt samples/example-output.gdt
/// aus den anonymisierten Testdaten neu, damit Dokumentation und Generator nie auseinanderlaufen.
/// </summary>
public class ExampleOutputTests
{
    private static string? FindSamplesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var samples = Path.Combine(directory.FullName, "samples");
            if (Directory.Exists(samples)) return samples;
            directory = directory.Parent;
        }
        return null;
    }

    [Fact]
    public void BeispielGdtWirdAusDenTestdatenErzeugt()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var samples = FindSamplesDirectory();
        if (samples is null) return;

        var settings = new GdtSettings { SenderId = "VIVIDT8", ReceiverId = "MEDOFF" };
        var report = TestData.CreateReport();
        var writer = new GdtFileWriter(settings);
        var content = writer.BuildContent(report);

        File.WriteAllBytes(Path.Combine(samples, "example-output.gdt"), writer.Encoding.GetBytes(content));

        var lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("01380006310", lines[0]);
        Assert.Equal(writer.Encoding.GetByteCount(content), int.Parse(lines.Single(l => l[3..7] == "8100")[7..]));

        // Die veroeffentlichte Beispieldatei muss die frei erfundenen Testdaten enthalten.
        // Schlaegt das fehl, wurden versehentlich echte Patientendaten eingesetzt.
        Assert.Contains("Muster", content, StringComparison.Ordinal);
        Assert.Contains("12345", content, StringComparison.Ordinal);
        Assert.Contains("1.2.826.0.1.3680043.9.9999", content, StringComparison.Ordinal);
    }
}
