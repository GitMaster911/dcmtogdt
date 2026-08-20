using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Filtering;
using DCMtoGDTReports.Core.Mapping;
using DCMtoGDTReports.Core.Models;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class MeasurementFilterTests
{
    private static MeasurementFilter Create(MeasurementFilterSettings settings)
        => new(settings, new GdtSettings(), new MeasurementMapper());

    private static MeasurementResult Value(
        string name, string code, double value, string site = "Left Ventricle",
        string mode = "2D mode", string selection = "", string shortName = "") => new()
    {
        Name = name,
        ShortName = string.IsNullOrEmpty(shortName) ? name : shortName,
        SourceCode = code,
        RawValue = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        Value = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        NumericValue = value,
        Unit = "%",
        FindingSite = site,
        ImageMode = mode,
        SelectionStatus = selection,
        Group = $"{site} / {mode}"
    };

    private static List<MeasurementResult> Sample() =>
    [
        Value("Left Ventricular Ejection Fraction", "LN:18043-0", 67.28, shortName: "EF", selection: "Most recent value chosen"),
        Value("Left Ventricular Ejection Fraction", "LN:18043-0", 49.82, shortName: "EF"),
        Value("Left Ventricular Ejection Fraction", "LN:18043-0", 58.16, shortName: "EF"),
        Value("Peak Longitudinal Strain", "99GEMS:GEU-106-0002", -18.1, "left ventricle apical anterior segment", shortName: "PLS"),
        Value("Peak Longitudinal Strain", "99GEMS:GEU-106-0002", -20.6, "left ventricle basal anterior segment", shortName: "PLS"),
        Value("Peak Velocity", "LN:11726-7", 2.11, "Tricuspid Valve", "Doppler Continuous Wave", shortName: "Vmax"),
        Value("Unbekannter GE-Wert", "99GEMS:X-9", 1.0)
    ];

    [Fact]
    public void DeaktivierterFilter_LiefertAlleMesswerte()
    {
        var source = Sample();
        var result = Create(new MeasurementFilterSettings { Enabled = false }).Apply(source);

        Assert.Same(source, result);
    }

    [Fact]
    public void ExcludeFindingSites_MitPlatzhalterEntferntDieStrainSegmente()
    {
        var result = Create(new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.All,
            ExcludeFindingSites = ["*segment"]
        }).Apply(Sample());

        Assert.DoesNotContain(result, m => m.FindingSite.EndsWith("segment", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void IncludeConcepts_TrifftAufKurznameCodeUndOriginalname()
    {
        var settings = new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.All,
            IncludeConcepts = ["EF", "LN:11726-7"]
        };

        var result = Create(settings).Apply(Sample());

        Assert.Equal(4, result.Count);
        Assert.All(result, m => Assert.True(m.ShortName is "EF" or "Vmax"));
    }

    [Fact]
    public void ExcludeConcepts_HatVorrangVorInclude()
    {
        var settings = new MeasurementFilterSettings
        {
            Enabled = true,
            IncludeConcepts = ["EF", "PLS"],
            ExcludeConcepts = ["PLS"]
        };

        var result = Create(settings).Apply(Sample());

        Assert.All(result, m => Assert.Equal("EF", m.ShortName));
    }

    [Fact]
    public void IncludeImageModes_FiltertNachAufnahmemodus()
    {
        var settings = new MeasurementFilterSettings { Enabled = true, IncludeImageModes = ["Doppler*"] };

        var result = Create(settings).Apply(Sample());

        Assert.Single(result);
        Assert.Equal("Vmax", result[0].ShortName);
    }

    [Fact]
    public void OnlySelectedValues_BehaeltNurVomGeraetGewaehlteWerte()
    {
        var result = Create(new MeasurementFilterSettings { Enabled = true, OnlySelectedValues = true }).Apply(Sample());

        Assert.Single(result);
        Assert.Equal("Most recent value chosen", result[0].SelectionStatus);
    }

    [Fact]
    public void OnlyMappedMeasurements_EntferntUngemappteMesswerte()
    {
        var result = Create(new MeasurementFilterSettings { Enabled = true, OnlyMappedMeasurements = true }).Apply(Sample());

        Assert.DoesNotContain(result, m => m.SourceCode == "99GEMS:X-9");
    }

    [Fact]
    public void RepeatedValuesMean_FasstWiederholungenZusammenUndVermerktDieAnzahl()
    {
        var result = Create(new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.Mean
        }).Apply(Sample());

        var ef = result.Single(m => m.ShortName == "EF");
        Assert.Equal("58.42", ef.Value);
        Assert.Equal("Mittel aus 3", ef.AggregationNote);
        Assert.Contains("Mittel aus 3", ef.ToDisplayLine(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RepeatedValueMode.First, "67.28")]
    [InlineData(RepeatedValueMode.Last, "58.16")]
    [InlineData(RepeatedValueMode.Min, "49.82")]
    [InlineData(RepeatedValueMode.Max, "67.28")]
    public void RepeatedValues_WaehltDenKonfiguriertenWert(RepeatedValueMode mode, string expected)
    {
        var settings = new MeasurementFilterSettings { Enabled = true, RepeatedValues = mode };

        var ef = Create(settings).Apply(Sample()).Single(m => m.ShortName == "EF");

        Assert.Equal(expected, ef.Value);
    }

    [Fact]
    public void RepeatedValuesMinMaxMean_ZeigtMittelwertMitSpannweiteUndAnzahl()
    {
        var settings = new MeasurementFilterSettings { Enabled = true, RepeatedValues = RepeatedValueMode.MinMaxMean };

        var ef = Create(settings).Apply(Sample()).Single(m => m.ShortName == "EF");

        Assert.Equal("58.42", ef.Value);
        Assert.Equal("Min 49.82 / Max 67.28, n=3", ef.AggregationNote);
        Assert.Equal("EF: 58.42 % (Min 49.82 / Max 67.28, n=3)", ef.ToDisplayLine());
    }

    [Fact]
    public void RepeatedValuesMinMaxMean_EinzelmessungBleibtOhneZusatz()
    {
        var settings = new MeasurementFilterSettings { Enabled = true, RepeatedValues = RepeatedValueMode.MinMaxMean };

        var vmax = Create(settings).Apply(Sample()).Single(m => m.ShortName == "Vmax");

        Assert.Empty(vmax.AggregationNote);
    }

    [Fact]
    public void RepeatedValues_TrenntNachFindingSite()
    {
        var settings = new MeasurementFilterSettings { Enabled = true, RepeatedValues = RepeatedValueMode.Mean };

        var result = Create(settings).Apply(Sample());

        // Die beiden Strain-Werte gehoeren zu unterschiedlichen Segmenten und bleiben getrennt.
        Assert.Equal(2, result.Count(m => m.ShortName == "PLS"));
    }

    [Fact]
    public void MaxMeasurements_BegrenztDieAnzahl()
    {
        var result = Create(new MeasurementFilterSettings { Enabled = true, MaxMeasurements = 3 }).Apply(Sample());

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ApplyFilteredMeasurements_MerktSichDieAnzahlDerEntfallenenWerte()
    {
        var report = TestData.CreateReport();
        var original = report.Measurements.Count;

        report.ApplyFilteredMeasurements(report.Measurements.Take(1).ToList());

        Assert.Single(report.Measurements);
        Assert.Equal(original - 1, report.FilteredOutCount);
    }
}
