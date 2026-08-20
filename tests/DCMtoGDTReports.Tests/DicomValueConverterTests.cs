using DCMtoGDTReports.Core.Dicom;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class DicomValueConverterTests
{
    [Theory]
    [InlineData("20240115", "15012024")]
    [InlineData("19850312", "12031985")]
    [InlineData("2024.01.15", "15012024")]
    [InlineData("", "")]
    [InlineData("2024", "")]
    [InlineData("20241332", "")]
    public void ToGdtDate_KonvertiertDicomDatum(string input, string expected)
    {
        Assert.Equal(expected, DicomValueConverter.ToGdtDate(input));
    }

    [Theory]
    [InlineData("145651", "145651")]
    [InlineData("145651.000000", "145651")]
    [InlineData("1456", "145600")]
    [InlineData("09", "090000")]
    [InlineData("", "")]
    [InlineData("991234", "")]
    public void ToGdtTime_KonvertiertDicomZeit(string input, string expected)
    {
        Assert.Equal(expected, DicomValueConverter.ToGdtTime(input));
    }

    [Theory]
    [InlineData("20240115", "15.01.2024")]
    [InlineData("", "")]
    public void ToDisplayDate_LiefertDeutschesFormat(string input, string expected)
    {
        Assert.Equal(expected, DicomValueConverter.ToDisplayDate(input));
    }

    [Theory]
    [InlineData("M", "1")]
    [InlineData("m", "1")]
    [InlineData("F", "2")]
    [InlineData("W", "2")]
    [InlineData("O", "")]
    [InlineData("", "")]
    public void ToGdtSex_MapptNurEindeutigeWerte(string input, string expected)
    {
        Assert.Equal(expected, DicomValueConverter.ToGdtSex(input));
    }

    [Theory]
    [InlineData("Meier^Anna", "Meier", "Anna")]
    [InlineData("Mustermann^Max^Peter", "Mustermann", "Max Peter")]
    [InlineData("Mustermann^Max^^Dr.^", "Mustermann", "Max")]
    [InlineData("Mustermann", "Mustermann", "")]
    [InlineData("", "", "")]
    public void ParsePersonName_ZerlegtDicomPersonenname(string input, string lastName, string firstName)
    {
        var (last, first) = DicomValueConverter.ParsePersonName(input);
        Assert.Equal(lastName, last);
        Assert.Equal(firstName, first);
    }

    [Fact]
    public void ParsePersonName_IgnoriertIdeografischeKomponenten()
    {
        var (last, first) = DicomValueConverter.ParsePersonName("Yamada^Tarou=\u5c71\u7530^\u592a\u90ce");
        Assert.Equal("Yamada", last);
        Assert.Equal("Tarou", first);
    }

    [Theory]
    [InlineData("79.206677328227", 2, ".", "79.21")]
    [InlineData("112.00000", 2, ".", "112")]
    [InlineData("0.63558862196369", 2, ".", "0.64")]
    [InlineData("-18.098783493042", 2, ".", "-18.1")]
    [InlineData("4.2141821355915", 2, ",", "4,21")]
    [InlineData("2.5970149364299", 0, ".", "3")]
    [InlineData("nicht numerisch", 2, ".", "nicht numerisch")]
    public void FormatNumeric_RundetUndFormatiert(string raw, int places, string separator, string expected)
    {
        Assert.Equal(expected, DicomValueConverter.FormatNumeric(raw, places, separator));
    }

    [Fact]
    public void ToDateTime_SetztDatumUndZeitZusammen()
    {
        var value = DicomValueConverter.ToDateTime("20240115", "103015");
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 15), value);
    }
}
