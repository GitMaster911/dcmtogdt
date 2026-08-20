using System.Text;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Templates;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class GdtTemplateTests
{
    static GdtTemplateTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static Encoding Latin1 => Encoding.GetEncoding(28591);

    private static IReadOnlyList<GdtField> Build(GdtTemplate template)
        => new Gdt6310Builder(new GdtSettings()) { Template = template }.BuildFields(TestData.CreateReport());

    private static GdtTemplate SingleLine(string fieldId, string content) => new()
    {
        Enabled = true,
        Lines = [new GdtTemplateLine { FieldId = fieldId, Content = content }]
    };

    [Fact]
    public void OhneAktivierteVorlage_WirdDerStandardaufbauVerwendet()
    {
        var template = GdtTemplate.CreateDefault();
        template.Enabled = false;

        var fields = Build(template);

        Assert.Contains(fields, f => f.FieldId == "8000" && f.Content == "6310");
        Assert.Contains(fields, f => f.FieldId == "8100" || f.FieldId == "8315");
    }

    [Fact]
    public void Standardvorlage_ErzeugtDieselbenKopfdatenWieDerStandardaufbau()
    {
        var template = GdtTemplate.CreateDefault();
        template.Enabled = true;

        var fields = Build(template);

        Assert.Equal("6310", fields.Single(f => f.FieldId == "8000").Content);
        Assert.Equal("12345", fields.Single(f => f.FieldId == "3000").Content);
        Assert.Equal("Muster", fields.Single(f => f.FieldId == "3101").Content);
        Assert.Equal("12031985", fields.Single(f => f.FieldId == "3103").Content);
        Assert.Equal("15012024", fields.Single(f => f.FieldId == "6200").Content);
    }

    [Fact]
    public void DeaktivierteZeilen_WerdenNichtAusgegeben()
    {
        var template = GdtTemplate.CreateDefault();
        template.Enabled = true;
        foreach (var line in template.Lines.Where(l => l.FieldId == "3101")) line.Enabled = false;

        var fields = Build(template);

        Assert.DoesNotContain(fields, f => f.FieldId == "3101");
        Assert.Contains(fields, f => f.FieldId == "3102");
    }

    [Fact]
    public void Platzhalter_WerdenDurchDieEchtenWerteErsetzt()
    {
        var fields = Build(SingleLine("6220", "Patient: {Nachname}, {Vorname} ({PatientNummer})"));

        Assert.Equal("Patient: Muster, Erika (12345)", fields.Single().Content);
    }

    [Fact]
    public void Platzhalter_KoennenFreiKombiniertWerden()
    {
        var fields = Build(SingleLine("6220", "{DatumLang} {ZeitLang} - {Modell}"));

        Assert.Equal("15.01.2024 10:30:15 - Vivid T8", fields.Single().Content);
    }

    [Fact]
    public void MesswertePlatzhalter_ErzeugtMehrereZeilen()
    {
        var fields = Build(SingleLine("6220", "{Messwerte}"));

        Assert.True(fields.Count > 1);
        Assert.All(fields, f => Assert.Equal("6220", f.FieldId));
        Assert.Contains(fields, f => f.Content.Contains("LVIDd", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Content.StartsWith('['));
    }

    [Fact]
    public void MesswerteOhneGruppen_LaesstDieUeberschriftenWeg()
    {
        var fields = Build(SingleLine("6220", "{MesswerteOhneGruppen}"));

        Assert.DoesNotContain(fields, f => f.Content.StartsWith('['));
        Assert.Contains(fields, f => f.Content.Contains("LVIDd", StringComparison.Ordinal));
    }

    [Fact]
    public void ZeilenMitLeeremPlatzhalter_WerdenWeggelassen()
    {
        var report = TestData.CreateReport();
        report.Header.AccessionNumber = string.Empty;

        var builder = new Gdt6310Builder(new GdtSettings())
        {
            Template = SingleLine("6220", "Accession: {Anforderungsnummer}")
        };

        Assert.Empty(builder.BuildFields(report));
    }

    [Fact]
    public void UngueltigeFeldkennung_WirdUebersprungenStattDieDateiZuZerstoeren()
    {
        var template = new GdtTemplate
        {
            Enabled = true,
            Lines =
            [
                new GdtTemplateLine { FieldId = "62", Content = "kaputt" },
                new GdtTemplateLine { FieldId = "6220", Content = "in Ordnung" }
            ]
        };

        var fields = Build(template);

        Assert.Single(fields);
        Assert.Equal("in Ordnung", fields[0].Content);
    }

    [Fact]
    public void ZuLangeZeilen_WerdenAufMehrereGdtZeilenVerteilt()
    {
        var settings = new GdtSettings { MaxResultLineLength = 30 };
        var builder = new Gdt6310Builder(settings)
        {
            Template = SingleLine("6220", "Sehr lange Zeile mit vielen Woertern die umgebrochen werden muss")
        };

        var fields = builder.BuildFields(TestData.CreateReport());

        Assert.True(fields.Count > 1);
        Assert.All(fields, f => Assert.True(f.Content.Length <= 30));
    }

    [Fact]
    public void VorlageErzeugtEineGueltigeGdtDatei()
    {
        var template = GdtTemplate.CreateDefault();
        template.Enabled = true;

        var content = new Gdt6310Builder(new GdtSettings()) { Template = template }
            .BuildRecord(TestData.CreateReport(), Latin1);

        var lines = content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
            Assert.Equal(Latin1.GetByteCount(line[7..]) + 9, int.Parse(line[..3]));

        Assert.Equal(Latin1.GetByteCount(content), int.Parse(lines.Single(l => l[3..7] == "8100")[7..]));
    }

    [Theory]
    [InlineData("6220", true)]
    [InlineData("8000", true)]
    [InlineData("620", false)]
    [InlineData("62200", false)]
    [InlineData("62a0", false)]
    [InlineData("", false)]
    public void FeldkennungWirdGeprueft(string fieldId, bool expected)
    {
        Assert.Equal(expected, GdtTemplateEngine.IsValidFieldId(fieldId));
    }

    [Fact]
    public void JederPlatzhalterLaesstSichAufloesen()
    {
        foreach (var placeholder in GdtTemplateEngine.Placeholders)
        {
            if (placeholder.Name is GdtTemplateEngine.MeasurementsPlaceholder
                or GdtTemplateEngine.MeasurementsFlatPlaceholder) continue;

            var fields = Build(SingleLine("6220", $"X{placeholder.Token}"));

            // "X" als Anker: die Zeile darf nicht mehr den Platzhalter selbst enthalten.
            Assert.All(fields, f => Assert.DoesNotContain(placeholder.Token, f.Content, StringComparison.Ordinal));
        }
    }
}
