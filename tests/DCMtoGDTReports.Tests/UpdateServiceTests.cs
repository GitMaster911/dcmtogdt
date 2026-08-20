using System.Text.Json;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Updating;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class UpdateServiceTests : IDisposable
{
    private readonly string _shareDirectory = Path.Combine(Path.GetTempPath(), $"dcm2gdt-update-{Guid.NewGuid():N}");

    public UpdateServiceTests() => Directory.CreateDirectory(_shareDirectory);

    private string WriteManifest(UpdateManifest manifest)
    {
        var path = Path.Combine(_shareDirectory, "update.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        return path;
    }

    [Fact]
    public async Task Check_OhneKonfiguration_MeldetNichtKonfiguriert()
    {
        var result = await new UpdateService(new UpdateSettings()).CheckAsync();

        Assert.Equal(UpdateAvailability.NotConfigured, result.Availability);
    }

    [Fact]
    public async Task Check_NeuereVersionAufFreigabe_WirdErkannt()
    {
        var path = WriteManifest(new UpdateManifest { Version = "99.9.9", PackageUrl = "paket.zip", Sha256 = "AB" });

        var result = await new UpdateService(new UpdateSettings { Enabled = true, ManifestUrl = path }).CheckAsync();

        Assert.Equal(UpdateAvailability.UpdateAvailable, result.Availability);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("99.9.9", result.Manifest!.Version);
    }

    [Fact]
    public async Task Check_AeltereVersion_MeldetAktuell()
    {
        var path = WriteManifest(new UpdateManifest { Version = "0.0.1", PackageUrl = "paket.zip", Sha256 = "AB" });

        var result = await new UpdateService(new UpdateSettings { Enabled = true, ManifestUrl = path }).CheckAsync();

        Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task Check_UngueltigeVersionsangabe_MeldetFehler()
    {
        var path = WriteManifest(new UpdateManifest { Version = "keine-version", PackageUrl = "p.zip", Sha256 = "AB" });

        var result = await new UpdateService(new UpdateSettings { Enabled = true, ManifestUrl = path }).CheckAsync();

        Assert.Equal(UpdateAvailability.Failed, result.Availability);
    }

    [Fact]
    public async Task Check_FehlendesManifest_MeldetFehlerStattAbsturz()
    {
        var settings = new UpdateSettings { Enabled = true, ManifestUrl = Path.Combine(_shareDirectory, "fehlt.json") };

        var result = await new UpdateService(settings).CheckAsync();

        Assert.Equal(UpdateAvailability.Failed, result.Availability);
    }

    [Fact]
    public void ResolvePackageLocation_RelativerPfadWirdAufManifestOrtBezogen()
    {
        var manifest = Path.Combine(_shareDirectory, "update.json");

        var resolved = UpdateService.ResolvePackageLocation(manifest, "DCMtoGDTReports-1.2.0.zip");

        Assert.Equal(Path.Combine(_shareDirectory, "DCMtoGDTReports-1.2.0.zip"), resolved);
    }

    [Fact]
    public void ResolvePackageLocation_RelativeAngabeBeiHttpsWirdZurAbsolutenUrl()
    {
        var resolved = UpdateService.ResolvePackageLocation(
            "https://updates.example.com/dcm2gdt/update.json", "DCMtoGDTReports-1.2.0.zip");

        Assert.Equal("https://updates.example.com/dcm2gdt/DCMtoGDTReports-1.2.0.zip", resolved);
    }

    [Fact]
    public void ResolvePackageLocation_AbsoluterPfadBleibtUnveraendert()
    {
        var absolute = Path.Combine(_shareDirectory, "paket.zip");

        Assert.Equal(absolute, UpdateService.ResolvePackageLocation("egal", absolute));
    }

    [Fact]
    public void ResolvePackageLocation_OhnePaketpfad_WirftAussagekraeftigenFehler()
    {
        Assert.Throws<InvalidOperationException>(() => UpdateService.ResolvePackageLocation("egal", string.Empty));
    }

    [Fact]
    public async Task DownloadAndStage_OhnePruefsumme_WirdAbgelehnt()
    {
        var manifest = new UpdateManifest { Version = "9.9.9", PackageUrl = "paket.zip", Sha256 = string.Empty };
        var settings = new UpdateSettings { Enabled = true, ManifestUrl = Path.Combine(_shareDirectory, "update.json") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateService(settings).DownloadAndStageAsync(manifest));
    }

    [Fact]
    public async Task DownloadAndStage_FalschePruefsumme_WirdAbgelehnt()
    {
        var packagePath = Path.Combine(_shareDirectory, "paket.zip");
        await File.WriteAllTextAsync(packagePath, "kein echtes Paket");

        var manifest = new UpdateManifest { Version = "9.9.9", PackageUrl = "paket.zip", Sha256 = new string('A', 64) };
        var settings = new UpdateSettings { Enabled = true, ManifestUrl = Path.Combine(_shareDirectory, "update.json") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateService(settings).DownloadAndStageAsync(manifest));

        Assert.Contains("SHA256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAndStage_GueltigesPaket_WirdEntpackt()
    {
        var contentDirectory = Path.Combine(_shareDirectory, "inhalt");
        Directory.CreateDirectory(contentDirectory);
        await File.WriteAllTextAsync(Path.Combine(contentDirectory, "DCMtoGDTReports.dll"), "neue Version");

        var packagePath = Path.Combine(_shareDirectory, "paket.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(contentDirectory, packagePath);

        await using var stream = File.OpenRead(packagePath);
        var sha256 = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream));
        await stream.DisposeAsync();

        var manifest = new UpdateManifest { Version = "9.9.9", PackageUrl = "paket.zip", Sha256 = sha256 };
        var settings = new UpdateSettings { Enabled = true, ManifestUrl = Path.Combine(_shareDirectory, "update.json") };

        var staging = await new UpdateService(settings).DownloadAndStageAsync(manifest);
        try
        {
            Assert.True(File.Exists(Path.Combine(staging, "DCMtoGDTReports.dll")));
            Assert.Equal("neue Version", await File.ReadAllTextAsync(Path.Combine(staging, "DCMtoGDTReports.dll")));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_shareDirectory)) Directory.Delete(_shareDirectory, recursive: true);
    }
}
