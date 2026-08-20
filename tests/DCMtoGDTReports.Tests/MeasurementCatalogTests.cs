using DCMtoGDTReports.Core.Catalog;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Filtering;
using DCMtoGDTReports.Core.Mapping;
using DCMtoGDTReports.Core.Models;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class MeasurementCatalogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dcm2gdt-catalog-{Guid.NewGuid():N}.json");

    private static MeasurementResult Value(string name, string code, string site, string mode) => new()
    {
        Name = name,
        ShortName = name,
        SourceCode = code,
        FindingSite = site,
        ImageMode = mode,
        Unit = "cm",
        RawValue = "1",
        Value = "1",
        NumericValue = 1,
        Group = $"{site} / {mode}"
    };

    private static SrReport Report(params MeasurementResult[] measurements) => new()
    {
        Header = new SrHeader(),
        Engine = "Test",
        Measurements = measurements,
        RawMeasurementCount = measurements.Length
    };

    private static SrReport SampleReport() => Report(
        Value("E/e'", "LN:59111-5", "Lateral Mitral Annulus", "Doppler Pulsed"),
        Value("E/e'", "LN:59111-5", "Medial Mitral Annulus", "Doppler Pulsed"),
        Value("LVIDd", "LN:29436-3", "Left Ventricle", "2D mode"));

    private static MeasurementFilter Filter(MeasurementCatalog catalog) => new(
        new MeasurementFilterSettings { Enabled = false },
        new GdtSettings(),
        new MeasurementMapper(),
        catalog);

    [Fact]
    public void Learn_NimmtMessgroessenRegionenUndModiAuf()
    {
        var catalog = new MeasurementCatalog();

        var added = MeasurementCatalogService.Learn(catalog, SampleReport());

        Assert.Equal(2, catalog.Measurements.Count);
        Assert.Equal(3, catalog.Regions.Count);
        Assert.Equal(2, catalog.ImageModes.Count);
        Assert.Equal(7, added);
        Assert.Equal(1, catalog.LearnedFileCount);
    }

    [Fact]
    public void Learn_NeueEintraegeSindImmerAusgewaehlt()
    {
        var catalog = new MeasurementCatalog();

        MeasurementCatalogService.Learn(catalog, SampleReport());

        Assert.All(catalog.AllEntries, e => Assert.True(e.Selected));
    }

    [Fact]
    public void Learn_ZaehltWiederholtesVorkommenOhneDoppeltAnzulegen()
    {
        var catalog = new MeasurementCatalog();

        MeasurementCatalogService.Learn(catalog, SampleReport());
        var addedSecondTime = MeasurementCatalogService.Learn(catalog, SampleReport());

        Assert.Equal(0, addedSecondTime);
        Assert.Equal(2, catalog.Measurements.Count);
        Assert.Equal(2, catalog.LearnedFileCount);
        Assert.All(catalog.Regions, r => Assert.Equal(2, r.SeenCount));
    }

    [Fact]
    public void Filter_OhneAktivierteAuswahl_BleibtAllesErhalten()
    {
        var catalog = new MeasurementCatalog();
        MeasurementCatalogService.Learn(catalog, SampleReport());
        catalog.Enabled = false;

        var result = Filter(catalog).Apply(SampleReport().Measurements);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Filter_AbgewaehlteRegionFaelltWeg()
    {
        var catalog = new MeasurementCatalog();
        MeasurementCatalogService.Learn(catalog, SampleReport());
        catalog.Enabled = true;
        catalog.Regions.Single(r => r.Key == "Medial Mitral Annulus").Selected = false;

        var result = Filter(catalog).Apply(SampleReport().Measurements);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, m => m.FindingSite == "Medial Mitral Annulus");
        Assert.Contains(result, m => m.FindingSite == "Lateral Mitral Annulus");
    }

    [Fact]
    public void Filter_AbgewaehlteMessgroesseFaelltWeg()
    {
        var catalog = new MeasurementCatalog();
        MeasurementCatalogService.Learn(catalog, SampleReport());
        catalog.Enabled = true;
        catalog.Measurements.Single(m => m.Key == "LN:29436-3").Selected = false;

        var result = Filter(catalog).Apply(SampleReport().Measurements);

        Assert.DoesNotContain(result, m => m.ShortName == "LVIDd");
    }

    [Fact]
    public void Filter_AbgewaehlterAufnahmemodusFaelltWeg()
    {
        var catalog = new MeasurementCatalog();
        MeasurementCatalogService.Learn(catalog, SampleReport());
        catalog.Enabled = true;
        catalog.ImageModes.Single(m => m.Key == "2D mode").Selected = false;

        var result = Filter(catalog).Apply(SampleReport().Measurements);

        Assert.All(result, m => Assert.Equal("Doppler Pulsed", m.ImageMode));
    }

    [Fact]
    public void Filter_UnbekannterMesswertWirdStandardmaessigUebernommen()
    {
        var catalog = new MeasurementCatalog { Enabled = true, IncludeUnknown = true };
        MeasurementCatalogService.Learn(catalog, SampleReport());

        var neu = Value("TAPSE", "99GEMS:NEU-1", "Tricuspid Valve", "M mode");

        Assert.Single(Filter(catalog).Apply([neu]));
    }

    [Fact]
    public void Filter_UnbekannterMesswertKannAusgeschlossenWerden()
    {
        var catalog = new MeasurementCatalog { Enabled = true, IncludeUnknown = false };
        MeasurementCatalogService.Learn(catalog, SampleReport());

        var neu = Value("TAPSE", "99GEMS:NEU-1", "Tricuspid Valve", "M mode");

        Assert.Empty(Filter(catalog).Apply([neu]));
    }

    [Fact]
    public void Filter_LeererKatalogAendertNichts()
    {
        var catalog = new MeasurementCatalog { Enabled = true };

        var result = Filter(catalog).Apply(SampleReport().Measurements);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SpeichernUndLaden_ErhaeltDieAuswahl()
    {
        var service = new MeasurementCatalogService(_path);
        var catalog = new MeasurementCatalog { Enabled = true, IncludeUnknown = false };
        MeasurementCatalogService.Learn(catalog, SampleReport());
        catalog.Regions.Single(r => r.Key == "Medial Mitral Annulus").Selected = false;

        service.Save(catalog);
        var loaded = service.Load();

        Assert.True(loaded.Enabled);
        Assert.False(loaded.IncludeUnknown);
        Assert.False(loaded.Regions.Single(r => r.Key == "Medial Mitral Annulus").Selected);
        Assert.True(loaded.Regions.Single(r => r.Key == "Lateral Mitral Annulus").Selected);
    }

    [Fact]
    public void Laden_BeschaedigteDatei_LiefertLeerenKatalogStattAbsturz()
    {
        File.WriteAllText(_path, "{ das ist kein gueltiges JSON");

        var loaded = new MeasurementCatalogService(_path).Load();

        Assert.True(loaded.IsEmpty);
    }

    [Fact]
    public void Laden_OhneDatei_LiefertLeerenKatalog()
    {
        Assert.True(new MeasurementCatalogService(_path).Load().IsEmpty);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
