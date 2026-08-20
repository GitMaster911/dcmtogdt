using DCMtoGDTReports.Core.Configuration;

namespace DCMtoGDTReports.Core.Processing;

/// <summary>
/// Prueft, ob eine neu aufgetauchte Datei vollstaendig geschrieben wurde.
/// Kriterium: Datei laesst sich exklusiv oeffnen und Groesse/LastWriteTime aendern sich nicht mehr.
/// </summary>
public sealed class FileStabilityChecker(ProcessingSettings settings)
{
    private readonly ProcessingSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<bool> WaitUntilStableAsync(string filePath, CancellationToken ct = default)
    {
        var pollDelay = TimeSpan.FromMilliseconds(Math.Clamp(_settings.FileStabilityPollMilliseconds, 100, 10_000));
        var maxAttempts = Math.Clamp(_settings.FileStabilityMaxAttempts, 1, 600);

        long lastSize = -1;
        DateTime lastWrite = DateTime.MinValue;
        var stableRounds = 0;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var info = new FileInfo(filePath);
            if (!info.Exists) return false;

            info.Refresh();
            if (info.Length == lastSize && info.LastWriteTimeUtc == lastWrite && info.Length > 0 && CanOpenExclusively(filePath))
            {
                // Zwei aufeinanderfolgende stabile Runden, damit langsame Netzwerkkopien nicht zu frueh gelesen werden.
                if (++stableRounds >= 2) return true;
            }
            else
            {
                stableRounds = 0;
            }

            lastSize = info.Length;
            lastWrite = info.LastWriteTimeUtc;
            await Task.Delay(pollDelay, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static bool CanOpenExclusively(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false; // Datei wird noch geschrieben.
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
