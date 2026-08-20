using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Mapping;
using DCMtoGDTReports.Core.Models;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class GermanNamesTests
{
    private static MeasurementResult Value(string site, string mode, string flow = "") => new()
    {
        Name = "Peak Velocity",
        ShortName = "Vmax",
        SourceCode = "LN:11726-7",
        FindingSite = site,
        ImageMode = mode,
        DirectionOfFlow = flow,
        Unit = "m/s",
        RawValue = "1.23",
        Value = "1.23",
        NumericValue = 1.23,
        Group = $"{site} / {mode}"
    };

    [Theory]
    [InlineData("Left Ventricle", "Linker Ventrikel")]
    [InlineData("Right Ventricle", "Rechter Ventrikel")]
    [InlineData("Left Atrium", "Linker Vorhof")]
    [InlineData("Aortic Valve", "Aortenklappe")]
    [InlineData("Mitral Valve", "Mitralklappe")]
    [InlineData("Tricuspid Valve", "Trikuspidalklappe")]
    [InlineData("Pulmonic Valve", "Pulmonalklappe")]
    [InlineData("Lateral Mitral Annulus", "Mitralklappenring lateral")]
    [InlineData("Medial Mitral Annulus", "Mitralklappenring septal")]
    [InlineData("Left Ventricle Outflow Tract", "LV-Ausflusstrakt")]
    [InlineData("left ventricle apical anterior segment", "LV apikal anterior")]
    [InlineData("left ventricle basal inferoseptal segment", "LV basal inferoseptal")]
    [InlineData("left ventricle mid anterolateral segment", "LV midventrikulaer anterolateral")]
    public void Regionen_WerdenUebersetzt(string original, string expected)
    {
        Assert.Equal(expected, new MeasurementMapper().ResolveRegion(original));
    }

    [Theory]
    [InlineData("2D mode", "2D")]
    [InlineData("M mode", "M-Mode")]
    [InlineData("Doppler Pulsed", "PW-Doppler")]
    [InlineData("Doppler Continuous Wave", "CW-Doppler")]
    public void Aufnahmemodi_WerdenUebersetzt(string original, string expected)
    {
        Assert.Equal(expected, new MeasurementMapper().ResolveImageMode(original));
    }

    [Theory]
    [InlineData("Antegrade Flow", "antegrad")]
    [InlineData("Regurgitant Flow", "Regurgitation")]
    public void Flussrichtung_WirdUebersetzt(string original, string expected)
    {
        Assert.Equal(expected, new MeasurementMapper().ResolveFlowDirection(original));
    }

    [Fact]
    public void UnbekannteRegion_BleibtUnveraendert()
    {
        Assert.Equal("Neue Region", new MeasurementMapper().ResolveRegion("Neue Region"));
    }

    [Fact]
    public void ApplyGermanNames_BildetDieGruppenbezeichnungNeu()
    {
        var measurements = new[] { Value("Lateral Mitral Annulus", "Doppler Pulsed") };

        new MeasurementMapper().ApplyGermanNames(measurements);

        Assert.Equal("Mitralklappenring lateral", measurements[0].FindingSite);
        Assert.Equal("PW-Doppler", measurements[0].ImageMode);
        Assert.Equal("Mitralklappenring lateral / PW-Doppler", measurements[0].Group);
    }

    [Fact]
    public void ApplyGermanNames_UebersetzteFlussrichtungStehtInDerErgebniszeile()
    {
        var measurements = new[] { Value("Aortic Valve", "Doppler Continuous Wave", "Antegrade Flow") };

        new MeasurementMapper().ApplyGermanNames(measurements);

        Assert.Equal("Vmax (antegrad): 1.23 m/s", measurements[0].ToDisplayLine());
    }

    [Fact]
    public void Apply_LaesstRegionenImOriginal_DamitFilterUndKatalogPassen()
    {
        var measurements = new[] { Value("Left Ventricle", "2D mode") };

        new MeasurementMapper().Apply(measurements, new GdtSettings());

        Assert.Equal("Left Ventricle", measurements[0].FindingSite);
        Assert.Equal("2D mode", measurements[0].ImageMode);
    }

    [Fact]
    public void EigeneBezeichnung_UeberschreibtDieVorgabe()
    {
        var mapper = new MeasurementMapper(
            regionMappings: new Dictionary<string, string> { ["Left Ventricle"] = "Linke Herzkammer" });

        Assert.Equal("Linke Herzkammer", mapper.ResolveRegion("Left Ventricle"));
    }

    [Fact]
    public void KatalogBezeichnung_WirdAlsNachschlagetabelleGeliefert()
    {
        var catalog = new MeasurementCatalog
        {
            Regions =
            [
                new CatalogEntry
                {
                    Key = "Left Ventricle",
                    DisplayName = "Left Ventricle",
                    CustomName = "Linke Herzkammer",
                    Kind = CatalogEntryKind.Region
                },
                new CatalogEntry
                {
                    Key = "Right Ventricle",
                    DisplayName = "Right Ventricle",
                    Kind = CatalogEntryKind.Region
                }
            ]
        };

        var names = MeasurementCatalogService.GetCustomNames(catalog, CatalogEntryKind.Region);

        Assert.Equal("Linke Herzkammer", names["Left Ventricle"]);
        Assert.DoesNotContain("Right Ventricle", names.Keys);
    }

    [Fact]
    public void KatalogBezeichnung_BleibtBeimLernenErhalten()
    {
        var catalog = new MeasurementCatalog();
        var report = new SrReport
        {
            Header = new SrHeader(),
            Engine = "Test",
            Measurements = [Value("Left Ventricle", "2D mode")],
            RawMeasurementCount = 1
        };

        MeasurementCatalogService.Learn(catalog, report);
        catalog.Regions.Single().CustomName = "Linke Herzkammer";
        MeasurementCatalogService.Learn(catalog, report);

        Assert.Equal("Linke Herzkammer", catalog.Regions.Single().CustomName);
        Assert.Equal(2, catalog.Regions.Single().SeenCount);
    }
}
