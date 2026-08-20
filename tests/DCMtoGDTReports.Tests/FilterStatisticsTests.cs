using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Filtering;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Mapping;
using DCMtoGDTReports.Core.Models;
using Xunit;

namespace DCMtoGDTReports.Tests;

/// <summary>
/// Bleibt am Ende nur eine Handvoll Messwerte im Befund, muss im Protokoll stehen, welche
/// Einstellung dafuer verantwortlich ist. Ohne die Aufschluesselung ist das im laufenden
/// Betrieb nicht zu klaeren.
/// </summary>
public class FilterStatisticsTests
{
    private static MeasurementResult Value(string name, string site = "", string mode = "", string selection = "")
        => new()
        {
            Name = name,
            ShortName = name,
            SourceCode = $"LN:{name}",
            Value = "1.0",
            RawValue = "1.0",
            NumericValue = 1.0,
            Unit = "cm",
            FindingSite = site,
            ImageMode = mode,
            SelectionStatus = selection
        };

    private static IReadOnlyList<MeasurementResult> Apply(
        MeasurementFilterSettings settings, IReadOnlyList<MeasurementResult> values, out FilterStatistics statistics)
        => new MeasurementFilter(settings, new GdtSettings(), new MeasurementMapper()).Apply(values, out statistics);

    [Fact]
    public void OhneFilter_WirdNichtsGezaehlt()
    {
        var values = new[] { Value("EF"), Value("LVIDd") };

        Apply(new MeasurementFilterSettings { Enabled = false }, values, out var statistics);

        Assert.False(statistics.RemovedAnything);
        Assert.Equal(string.Empty, statistics.Describe());
        Assert.Equal(2, statistics.Output);
    }

    [Fact]
    public void MusterAusschluss_WirdDerRichtigenStufeZugeordnet()
    {
        var values = new[]
        {
            Value("EF", site: "Left Ventricle"),
            Value("Strain", site: "Basal segment"),
            Value("Strain2", site: "Apical segment")
        };
        var settings = new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.All,
            ExcludeFindingSites = ["*segment"]
        };

        Apply(settings, values, out var statistics);

        Assert.Equal(2, statistics.RemovedBy(FilterReason.SitePattern));
        Assert.Equal(0, statistics.RemovedBy(FilterReason.ConceptPattern));
        Assert.Equal(1, statistics.Output);
        Assert.Contains("Muster Region: 2", statistics.Describe());
    }

    [Fact]
    public void OnlySelected_WirdEigenGezaehlt()
    {
        var values = new[] { Value("EF", selection: "SELECTED"), Value("LVIDd"), Value("IVSd") };
        var settings = new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.All,
            OnlySelectedValues = true
        };

        Apply(settings, values, out var statistics);

        Assert.Equal(2, statistics.RemovedBy(FilterReason.OnlySelected));
        Assert.Equal(1, statistics.Output);
    }

    [Fact]
    public void Obergrenze_WirdAusgewiesen()
    {
        var values = Enumerable.Range(1, 10).Select(i => Value($"M{i}")).ToArray();
        var settings = new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.All,
            MaxMeasurements = 3
        };

        Apply(settings, values, out var statistics);

        Assert.Equal(7, statistics.RemovedByLimit);
        Assert.Equal(3, statistics.Output);
        Assert.Contains("Obergrenze: 7", statistics.Describe());
    }

    [Fact]
    public void Zusammenfassen_WirdEigenAusgewiesen()
    {
        var values = new[] { Value("EF"), Value("EF"), Value("EF") };
        var settings = new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.MinMaxMean
        };

        Apply(settings, values, out var statistics);

        Assert.Equal(2, statistics.CondensedAway);
        Assert.Equal(1, statistics.Output);
        Assert.Contains("zusammengefasst: 2", statistics.Describe());
    }

    [Fact]
    public void Beschreibung_IstNachAnzahlSortiert()
    {
        var values = new[]
        {
            Value("EF", site: "Left Ventricle", mode: "2D"),
            Value("S1", site: "Basal segment", mode: "2D"),
            Value("S2", site: "Mid segment", mode: "2D"),
            Value("D1", site: "Left Ventricle", mode: "Doppler")
        };
        var settings = new MeasurementFilterSettings
        {
            Enabled = true,
            RepeatedValues = RepeatedValueMode.All,
            ExcludeFindingSites = ["*segment"],
            ExcludeImageModes = ["Doppler"]
        };

        Apply(settings, values, out var statistics);

        Assert.StartsWith("Muster Region: 2", statistics.Describe());
        Assert.Contains("Muster Aufnahmemodus: 1", statistics.Describe());
    }
}
