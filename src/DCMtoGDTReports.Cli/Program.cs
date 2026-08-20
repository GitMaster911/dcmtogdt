using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Gdt;
using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Core.Processing;
using DCMtoGDTReports.Core.Registry;
using DCMtoGDTReports.Core.Updating;
using DCMtoGDTReports.Tools;
using Microsoft.Extensions.Logging;

namespace DCMtoGDTReports.Cli;

/// <summary>
/// Konsolen-Testlauf: SR*.dcm -> Auswertung -> Messwerte auf der Konsole -> GDT-Testdatei.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FEHLER: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
        var options = ParseOptions(args.Skip(1).ToArray());

        var settingsService = new SettingsService(options.GetValueOrDefault("settings"));
        var settings = settingsService.Load();
        ApplyOverrides(settings, options);

        return command switch
        {
            "analyze" => await AnalyzeAsync(settings, options).ConfigureAwait(false),
            "gdt" => await GenerateGdtAsync(settings, settingsService, options).ConfigureAwait(false),
            "process" => await ProcessAsync(settings, options).ConfigureAwait(false),
            "watch" => await WatchAsync(settings).ConfigureAwait(false),
            "dcmtk" => ShowDcmtkStatus(settings),
            "update" => await CheckUpdateAsync(settings).ConfigureAwait(false),
            "pack" => CreateUpdatePackage(options),
            "config" => ShowConfig(settingsService, settings),
            _ => ShowHelp()
        };
    }

    private static async Task<int> AnalyzeAsync(AppSettings settings, Dictionary<string, string> options)
    {
        var file = ResolveInputFile(settings, options);
        Console.WriteLine($"Analysiere: {file}");

        var processor = new SrFileProcessor(settings, CreateRegistry(settings));
        var report = await processor.AnalyzeAsync(file, options.GetValueOrDefault("xml")).ConfigureAwait(false);

        PrintReport(report);
        return 0;
    }

    private static async Task<int> GenerateGdtAsync(AppSettings settings, SettingsService settingsService, Dictionary<string, string> options)
    {
        var file = ResolveInputFile(settings, options);
        var outputFolder = options.GetValueOrDefault("out")
            ?? (string.IsNullOrWhiteSpace(settings.OutputFolder) ? Path.Combine(Environment.CurrentDirectory, "out") : settings.OutputFolder);

        settings.OutputFolder = outputFolder;
        settingsService.ApplyFallbacks(settings);

        var processor = new SrFileProcessor(settings, CreateRegistry(settings));
        var report = await processor.AnalyzeAsync(file, options.GetValueOrDefault("xml")).ConfigureAwait(false);

        PrintReport(report);

        var writer = new GdtFileWriter(settings.Gdt);
        var path = writer.Write(report, outputFolder);

        Console.WriteLine();
        Console.WriteLine($"GDT-Testdatei erzeugt: {path}");
        Console.WriteLine($"Kodierung: {writer.Encoding.WebName} (Codepage {writer.Encoding.CodePage})");
        Console.WriteLine();
        Console.WriteLine("--- Inhalt ---");
        Console.WriteLine(writer.BuildContent(report).Replace("\r\n", Environment.NewLine));
        return 0;
    }

    private static async Task<int> ProcessAsync(AppSettings settings, Dictionary<string, string> options)
    {
        SettingsService.EnsureFolders(settings);
        var registry = CreateRegistry(settings);
        var processor = new SrFileProcessor(settings, registry, CreateLogger<SrFileProcessor>());

        var files = options.TryGetValue("file", out var single)
            ? new[] { single }
            : Directory.EnumerateFiles(settings.InputFolder, settings.Processing.FilePattern).ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"Keine Dateien mit Muster '{settings.Processing.FilePattern}' in '{settings.InputFolder}'.");
            return 0;
        }

        foreach (var file in files)
        {
            var result = await processor.ProcessAsync(file).ConfigureAwait(false);
            Console.WriteLine($"{result.StatusText,-20} {Path.GetFileName(file)} {result.GdtFilePath ?? result.ErrorMessage ?? string.Empty}");
        }

        return 0;
    }

    private static async Task<int> WatchAsync(AppSettings settings)
    {
        SettingsService.EnsureFolders(settings);
        var registry = CreateRegistry(settings);
        var processor = new SrFileProcessor(settings, registry, CreateLogger<SrFileProcessor>());

        using var watcher = new FolderWatcherService(settings, processor, CreateLogger<FolderWatcherService>());
        watcher.FileProcessed += (_, result) =>
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {result.StatusText,-20} {Path.GetFileName(result.SourceFilePath)}");

        watcher.Start();
        Console.WriteLine($"Ordnerueberwachung laeuft auf '{settings.InputFolder}'. Beenden mit Strg+C.");

        var stopSignal = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopSignal.TrySetResult(); };
        await stopSignal.Task.ConfigureAwait(false);

        watcher.Stop();
        return 0;
    }

    private static int ShowDcmtkStatus(AppSettings settings)
    {
        var installation = DcmtkLocator.Locate(settings.DcmtkPath);
        if (installation is null)
        {
            Console.WriteLine(DcmtkLocator.BuildNotFoundHint());
            Console.WriteLine();
            Console.WriteLine($"Zielordner fuer eine lokale Installation: {DcmtkLocator.GetBundledInstallDirectory()}");
            return 2;
        }

        Console.WriteLine($"DCMTK gefunden ({installation.Source}):");
        Console.WriteLine($"  {installation.Dsr2XmlPath}");
        Console.WriteLine($"  {installation.DcmDumpPath ?? "dcmdump.exe nicht vorhanden"}");
        return 0;
    }

    private static int ShowConfig(SettingsService service, AppSettings settings)
    {
        Console.WriteLine($"Konfigurationsdatei: {service.SettingsFilePath}");
        Console.WriteLine($"  Eingang : {settings.InputFolder}");
        Console.WriteLine($"  Ausgabe : {settings.OutputFolder}");
        Console.WriteLine($"  Archiv  : {settings.ArchiveFolder}");
        Console.WriteLine($"  Fehler  : {settings.ErrorFolder}");
        Console.WriteLine($"  Registry: {settings.RegistryDatabasePath}");
        Console.WriteLine($"  Engine  : {settings.Processing.PreferredEngine}");
        Console.WriteLine($"  Filter  : {(settings.MeasurementFilter.Enabled ? "aktiv" : "inaktiv")}, Wiederholungen: {settings.MeasurementFilter.RepeatedValues}");

        foreach (var problem in SettingsService.Validate(settings))
            Console.WriteLine($"  ! {problem}");

        return 0;
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
            DCMtoGDTReports - DICOM SR (GE Vivid T8) nach GDT 6310 fuer MEDICAL OFFICE

            Verwendung: dcm2gdt <befehl> [optionen]

            Befehle:
              analyze   SR-Datei auswerten und Messwerte anzeigen
              gdt       SR-Datei auswerten und GDT-Testdatei erzeugen
              process   Alle Dateien im Eingangsordner verarbeiten
              watch     Ordnerueberwachung starten
              dcmtk     DCMTK-Status anzeigen
              update    Auf neue Programmversion pruefen
              pack      Updatepaket (ZIP + update.json) aus einem Publish-Ordner erzeugen
              config    Aktuelle Konfiguration anzeigen

            Optionen:
              --file <pfad>      SR-Datei (sonst erste Datei im Eingangsordner)
              --out <ordner>     Ausgabeordner fuer die GDT-Datei bzw. das Updatepaket
              --xml <pfad>       Struktur-XML zusaetzlich speichern
              --settings <pfad>  Alternative settings.json
              --input <ordner>   Eingangsordner bzw. Publish-Ordner fuer "pack"
              --version <x.y.z>  Versionsnummer fuer "pack"
              --notes <text>     Aenderungshinweise fuer "pack"
            """);
        return 0;
    }

    private static async Task<int> CheckUpdateAsync(AppSettings settings)
    {
        var result = await new UpdateService(settings.Update).CheckAsync().ConfigureAwait(false);

        Console.WriteLine($"Installierte Version: {result.InstalledVersion.ToString(3)}");
        Console.WriteLine($"Quelle              : {(string.IsNullOrWhiteSpace(settings.Update.ManifestUrl) ? "(nicht konfiguriert)" : settings.Update.ManifestUrl)}");
        Console.WriteLine($"Ergebnis            : {result.Message}");

        if (result.Manifest is { Notes.Length: > 0 })
            Console.WriteLine($"Aenderungen         : {result.Manifest.Notes}");

        return result.Availability switch
        {
            UpdateAvailability.UpdateAvailable => 10,
            UpdateAvailability.Failed => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Erzeugt aus einem Publish-Ordner ein Updatepaket: <c>DCMtoGDTReports-x.y.z.zip</c> plus
    /// <c>update.json</c> mit passender SHA256. Das Ergebnis wird auf die Verteilfreigabe kopiert.
    /// </summary>
    private static int CreateUpdatePackage(Dictionary<string, string> options)
    {
        var sourceFolder = options.GetValueOrDefault("input")
            ?? throw new InvalidOperationException("Bitte den Publish-Ordner mit --input angeben.");
        var targetFolder = options.GetValueOrDefault("out")
            ?? throw new InvalidOperationException("Bitte den Zielordner mit --out angeben.");

        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"Publish-Ordner nicht gefunden: {sourceFolder}");

        var version = options.GetValueOrDefault("version")
            ?? FileVersionInfo.GetVersionInfo(
                Directory.EnumerateFiles(sourceFolder, "DCMtoGDTReports*.dll").First()).FileVersion
            ?? "1.0.0";

        Directory.CreateDirectory(targetFolder);
        var zipPath = Path.Combine(targetFolder, $"DCMtoGDTReports-{version}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        ZipFile.CreateFromDirectory(sourceFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        using var stream = File.OpenRead(zipPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(stream));

        var manifest = new UpdateManifest
        {
            Version = version,
            PackageUrl = Path.GetFileName(zipPath),
            Sha256 = sha256,
            Notes = options.GetValueOrDefault("notes") ?? string.Empty,
            PublishedAt = DateTimeOffset.Now
        };

        var manifestPath = Path.Combine(targetFolder, "update.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Paket    : {zipPath}");
        Console.WriteLine($"SHA256   : {sha256}");
        Console.WriteLine($"Manifest : {manifestPath}");
        Console.WriteLine();
        Console.WriteLine("Diesen Ordner als Update-Quelle in den Einstellungen hinterlegen:");
        Console.WriteLine($"  {manifestPath}");
        return 0;
    }

    private static void PrintReport(SrReport report)
    {
        var header = report.Header;
        Console.WriteLine();
        Console.WriteLine($"Engine            : {report.Engine}");
        Console.WriteLine($"Dokument          : {header.DocumentTitle}");
        Console.WriteLine($"Geraet            : {header.DeviceDescription} ({header.StationName})");
        Console.WriteLine($"Patient-Nr        : {header.PatientId}");
        Console.WriteLine($"Untersuchung      : {DicomValueConverter.ToDisplayDate(header.StudyDate)} {DicomValueConverter.ToDisplayTime(header.StudyTime)}");
        Console.WriteLine($"Accession         : {header.AccessionNumber}");
        Console.WriteLine($"SOPInstanceUID    : {header.SopInstanceUid}");
        Console.WriteLine($"Messwerte         : {report.Measurements.Count} (aus {report.RawMeasurementCount} SR-Knoten)");
        if (report.FilteredOutCount > 0)
            Console.WriteLine($"Durch Filter      : {report.FilteredOutCount} entfallen");
        if (report.DebugXmlPath is not null)
            Console.WriteLine($"Struktur-XML      : {report.DebugXmlPath}");
        Console.WriteLine();

        foreach (var group in report.Measurements.GroupBy(m => m.Group))
        {
            Console.WriteLine($"[{group.Key}]");
            foreach (var measurement in group)
                Console.WriteLine($"  {measurement.ToDisplayLine()}   <- {measurement.Name} ({measurement.SourceCode})");
            Console.WriteLine();
        }
    }

    private static IProcessedFileRegistry CreateRegistry(AppSettings settings)
    {
        var registry = new SqliteProcessedFileRegistry(settings.RegistryDatabasePath);
        registry.Initialize();
        return registry;
    }

    private static ILogger<T> CreateLogger<T>() =>
        LoggerFactory.Create(builder => builder.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ")).CreateLogger<T>();

    /// <summary>Sucht die auszuwertende Datei: --file, sonst erste passende Datei im Eingangsordner.</summary>
    private static string ResolveInputFile(AppSettings settings, Dictionary<string, string> options)
    {
        if (options.TryGetValue("file", out var file))
        {
            if (!File.Exists(file)) throw new FileNotFoundException($"Datei nicht gefunden: {file}", file);
            return file;
        }

        if (string.IsNullOrWhiteSpace(settings.InputFolder) || !Directory.Exists(settings.InputFolder))
            throw new InvalidOperationException("Kein Eingangsordner konfiguriert. Bitte --file angeben.");

        return Directory.EnumerateFiles(settings.InputFolder, settings.Processing.FilePattern).FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"Keine Datei mit Muster '{settings.Processing.FilePattern}' in '{settings.InputFolder}' gefunden.");
    }

    private static void ApplyOverrides(AppSettings settings, Dictionary<string, string> options)
    {
        if (options.TryGetValue("input", out var input)) settings.InputFolder = input;
        if (options.TryGetValue("out", out var output)) settings.OutputFolder = output;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;

            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            options[key] = value;
        }
        return options;
    }
}
