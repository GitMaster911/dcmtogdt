using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Models;
using DCMtoGDTReports.Core.Processing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DCMtoGDTReports.Worker;

/// <summary>
/// Hintergrunddienst, der den Eingangsordner dauerhaft ueberwacht.
/// Laeuft als Windows-Dienst oder als Konsolenanwendung.
/// </summary>
public sealed class SrWatcherWorker(
    AppSettings settings,
    FolderWatcherService watcher,
    ILogger<SrWatcherWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var problem in SettingsService.Validate(settings))
            logger.LogWarning("Konfigurationsproblem: {Problem}", problem);

        SettingsService.EnsureFolders(settings);

        watcher.FileProcessed += OnFileProcessed;
        watcher.Start();

        stoppingToken.Register(() =>
        {
            watcher.FileProcessed -= OnFileProcessed;
            watcher.Stop();
        });

        return Task.CompletedTask;
    }

    private void OnFileProcessed(object? sender, ProcessingResult result)
    {
        // Bewusst nur Dateiname und Status - keine Patientendaten ins Log.
        var fileName = Path.GetFileName(result.SourceFilePath);
        switch (result.Status)
        {
            case ProcessingStatus.Processed:
                logger.LogInformation("{Status}: {File} -> {Gdt}", result.StatusText, fileName, Path.GetFileName(result.GdtFilePath));
                break;
            case ProcessingStatus.Failed:
                logger.LogError("{Status}: {File} ({Error})", result.StatusText, fileName, result.ErrorMessage);
                break;
            default:
                logger.LogInformation("{Status}: {File}", result.StatusText, fileName);
                break;
        }
    }
}
