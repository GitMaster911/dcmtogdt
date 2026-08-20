using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Updating;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DCMtoGDTReports.Worker;

/// <summary>
/// Prueft zyklisch, ob eine neue Programmversion bereitsteht. Installiert wird nur, wenn das
/// in der Konfiguration ausdruecklich erlaubt ist - sonst erscheint lediglich ein Logeintrag.
/// </summary>
public sealed class UpdateWorker(
    AppSettings settings,
    ILogger<UpdateWorker> logger,
    ILoggerFactory loggerFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Update.Enabled || string.IsNullOrWhiteSpace(settings.Update.ManifestUrl))
        {
            logger.LogInformation("Updatepruefung ist nicht konfiguriert.");
            return;
        }

        var interval = settings.Update.CheckIntervalHours > 0
            ? TimeSpan.FromHours(settings.Update.CheckIntervalHours)
            : Timeout.InfiniteTimeSpan;

        var service = new UpdateService(settings.Update, logger: loggerFactory.CreateLogger<UpdateService>());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await service.CheckAsync(stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Updatepruefung: {Message}", result.Message);

                if (result.IsUpdateAvailable && settings.Update.InstallAutomatically)
                    await InstallAsync(service, result.Manifest!, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Updatepruefung fehlgeschlagen.");
            }

            if (interval == Timeout.InfiniteTimeSpan) return;

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task InstallAsync(UpdateService service, UpdateManifest manifest, CancellationToken ct)
    {
        var staging = await service.DownloadAndStageAsync(manifest, ct: ct).ConfigureAwait(false);

        // Das Skript stoppt den Dienst, tauscht die Dateien und startet ihn wieder.
        new UpdateInstaller(loggerFactory.CreateLogger<UpdateInstaller>())
            .Install(staging, serviceName: settings.Update.ServiceName);

        logger.LogWarning("Update {Version} wird installiert, der Dienst wird dafuer neu gestartet.", manifest.Version);
    }
}
