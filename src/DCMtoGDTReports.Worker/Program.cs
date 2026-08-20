using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Processing;
using DCMtoGDTReports.Core.Registry;
using DCMtoGDTReports.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

// Die Konfigurationsdatei kann per Argument abweichend gesetzt werden: --settings <pfad>
var settingsPath = GetArgument(args, "--settings");
var settingsService = new SettingsService(settingsPath);
var settings = settingsService.Load();
SettingsService.EnsureFolders(settings);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(settings.LogFolder, "worker-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31)
    .CreateLogger();

// Als Erstes in die Logdatei, damit bei Fehlersuche sofort ersichtlich ist,
// welche Konfiguration der Dienst tatsaechlich verwendet.
Log.Information("Konfiguration: {Path}", settingsService.SettingsFilePath);
Log.Information("Eingang {Input}, Ausgabe {Output}, Weiterleitung {Forward}, Muster {Pattern}",
    settings.InputFolder, settings.OutputFolder,
    string.IsNullOrWhiteSpace(settings.Processing.ForwardFolder) ? "nicht konfiguriert" : settings.Processing.ForwardFolder,
    settings.Processing.FilePattern);

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();
    builder.Services.AddWindowsService(options => options.ServiceName = "DCMtoGDTReports");

    builder.Services.AddSingleton(settingsService);
    builder.Services.AddSingleton(settings);
    builder.Services.AddSingleton<IProcessedFileRegistry>(_ =>
    {
        var registry = new SqliteProcessedFileRegistry(settings.RegistryDatabasePath);
        registry.Initialize();
        return registry;
    });
    builder.Services.AddSingleton<SrFileProcessor>();
    builder.Services.AddSingleton<FolderWatcherService>();
    builder.Services.AddHostedService<SrWatcherWorker>();
    builder.Services.AddHostedService<UpdateWorker>();

    await builder.Build().RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Der Dienst konnte nicht gestartet werden.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static string? GetArgument(string[] args, string name)
{
    var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
