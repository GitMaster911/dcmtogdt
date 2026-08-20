using System.Text;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Models;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class GdtLineFormatterTests
{
    static GdtLineFormatterTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static Encoding Latin1 => Encoding.GetEncoding(28591);

    [Fact]
    public void Format_SatzartZeile_EntsprichtGdtBeispiel()
    {
        // Beispiel aus der GDT-Spezifikation: Feld 8000 mit Inhalt "6310".
        Assert.Equal("01380006310\r\n", GdtLineFormatter.Format("8000", "6310", Latin1));
    }

    [Theory]
    [InlineData("", 9)]
    [InlineData("6310", 13)]
    [InlineData("MEDOFF", 15)]
    [InlineData("18082026", 17)]
    public void CalculateLength_IstInhaltslaengePlusNeun(string content, int expected)
    {
        Assert.Equal(expected, GdtLineFormatter.CalculateLength(content, Latin1));
    }

    [Fact]
    public void CalculateLength_RechnetInBytesDerZielkodierung()
    {
        // Umlaute sind in ISO-8859-1 einbyteig - die Laenge darf nicht von UTF-8 abweichen.
        Assert.Equal(9 + 5, GdtLineFormatter.CalculateLength("Gr\u00f6\u00dfe", Latin1));
    }

    [Fact]
    public void Format_ZeileEndetMitCrLf()
    {
        Assert.EndsWith("\r\n", GdtLineFormatter.Format("6220", "Test", Latin1), StringComparison.Ordinal);
    }

    [Fact]
    public void Format_UngueltigeFeldkennung_WirftAusnahme()
    {
        Assert.Throws<ArgumentException>(() => GdtLineFormatter.Format("620", "Test", Latin1));
        Assert.Throws<ArgumentException>(() => GdtLineFormatter.Format("62X0", "Test", Latin1));
    }

    [Fact]
    public void Format_ZuLangerInhalt_WirftAusnahme()
    {
        Assert.Throws<ArgumentException>(() =>
            GdtLineFormatter.Format("6220", new string('x', GdtField.MaxContentLength + 1), Latin1));
    }

    [Fact]
    public void BuildRecord_EnthaeltPflichtfelderUndKorrekteSatzlaenge()
    {
        var settings = new GdtSettings { SenderId = "VIVIDT8", ReceiverId = "MEDOFF" };
        var report = TestData.CreateReport();

        var content = new Gdt6310Builder(settings).BuildRecord(report, Latin1);
        var lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("01380006310", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("0158315MEDOFF", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("0168316VIVIDT8", StringComparison.Ordinal));
        Assert.Contains(lines, l => l[3..7] == "9218" && l[7..] == "02.10");

        // Jede Zeile muss ihre eigene Laenge korrekt angeben.
        foreach (var line in lines)
            Assert.Equal(line.Length + 2, int.Parse(line[..3]));

        // Feld 8100 muss der Gesamtlaenge des Satzes in Bytes entsprechen.
        var recordLengthLine = lines.Single(l => l[3..7] == "8100");
        Assert.Equal(Latin1.GetByteCount(content), int.Parse(recordLengthLine[7..]));
    }

    [Fact]
    public void BuildRecord_PatientenUndUntersuchungsfelderWerdenGesetzt()
    {
        var content = new Gdt6310Builder(new GdtSettings()).BuildRecord(TestData.CreateReport(), Latin1);
        var lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("12345", FieldValue(lines, "3000"));
        Assert.Equal("Muster", FieldValue(lines, "3101"));
        Assert.Equal("Erika", FieldValue(lines, "3102"));
        Assert.Equal("12031985", FieldValue(lines, "3103"));
        Assert.Equal("2", FieldValue(lines, "3110"));
        Assert.Equal("15012024", FieldValue(lines, "6200"));
        Assert.Equal("103015", FieldValue(lines, "6201"));
    }

    [Fact]
    public void BuildRecord_MesswerteStehenInFeld6220()
    {
        var content = new Gdt6310Builder(new GdtSettings()).BuildRecord(TestData.CreateReport(), Latin1);
        var resultLines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l[3..7] == "6220")
            .Select(l => l[7..])
            .ToList();

        Assert.Contains(resultLines, l => l.Contains("LVIDd: 4.21 cm", StringComparison.Ordinal));
        Assert.Contains(resultLines, l => l.Contains("Datum: 15.01.2024", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRecord_KeineZeileUeberschreitetDieMaximallaenge()
    {
        var settings = new GdtSettings { MaxResultLineLength = 40 };
        var report = TestData.CreateReport();
        report.Header.StudyDescription = new string('A', 300);

        var content = new Gdt6310Builder(settings).BuildRecord(report, Latin1);

        foreach (var line in content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Where(l => l[3..7] == "6220"))
            Assert.True(line[7..].Length <= 40, $"Zeile zu lang: {line}");
    }

    private static string FieldValue(IEnumerable<string> lines, string fieldId)
        => lines.Single(l => l[3..7] == fieldId)[7..];
}
