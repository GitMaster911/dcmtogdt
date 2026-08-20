using System.Collections.Concurrent;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DCMtoGDTReports.Core.Processing;

/// <summary>
/// Ueberwacht den Eingangsordner. Neue Dateien werden ueber einen FileSystemWatcher erkannt und
/// zusaetzlich zyklisch nachgescannt, damit keine Datei verloren geht (z. B. bei Netzlaufwerken).
/// Die eigentliche Verarbeitung laeuft seriell in einer Queue.
/// </summary>
public sealed class FolderWatcherService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SrFileProcessor _processor;
    private readonly FileStabilityChecker _stabilityChecker;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentQueue<string> _queue = new();

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private Task? _rescanTask;

    public FolderWatcherService(AppSettings settings, SrFileProcessor processor, ILogger<FolderWatcherService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _stabilityChecker = new FileStabilityChecker(settings.Processing);
        _logger = logger ?? NullLogger<FolderWatcherService>.Instance;
    }

    public bool IsRunning => _workerTask is { IsCompleted: false };

    /// <summary>Wird nach jeder verarbeiteten Datei ausgeloest (fuer GUI-Aktualisierung).</summary>
    public event EventHandler<ProcessingResult>? FileProcessed;

    public void Start()
    {
        if (IsRunning) return;

        if (string.IsNullOrWhiteSpace(_settings.InputFolder) || !Directory.Exists(_settings.InputFolder))
            throw new DirectoryNotFoundException($"Eingangsordner nicht gefunden: '{_settings.InputFolder}'");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _watcher = new FileSystemWatcher(_settings.InputFolder, _settings.Processing.FilePattern)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.Error += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher-Fehler im Eingangsordner.");

        _workerTask = Task.Run(() => WorkerLoopAsync(token), token);
        _rescanTask = Task.Run(() => RescanLoopAsync(token), token);

        _logger.LogInformation("Ordnerueberwachung gestartet: {Folder} (Muster {Pattern}).",
            _settings.InputFolder, _settings.Processing.FilePattern);

        EnqueueExistingFiles();
    }

    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _cts?.Cancel();
        try
        {
            var pending = new[] { _workerTask, _rescanTask }.Where(t => t is not null).Select(t => t!).ToArray();
            if (pending.Length > 0) Task.WaitAll(pending, TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // Abbruch beim Stoppen ist erwartet.
        }

        _workerTask = null;
        _rescanTask = null;
        _cts?.Dispose();
        _cts = null;

        _logger.LogInformation("Ordnerueberwachung gestoppt.");
    }

    /// <summary>Nimmt alle bereits im Eingangsordner liegenden Dateien in die Warteschlange auf.</summary>
    public void EnqueueExistingFiles()
    {
        if (!Directory.Exists(_settings.InputFolder)) return;

        foreach (var file in Directory.EnumerateFiles(_settings.InputFolder, _settings.Processing.FilePattern))
            Enqueue(file);
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);

    private void Enqueue(string path)
    {
        if (!_queued.TryAdd(path, 0)) return;
        _queue.Enqueue(path);
        _signal.Release();
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(ct).ConfigureAwait(false);
                if (!_queue.TryDequeue(out var path)) continue;

                try
                {
                    await ProcessQueuedFileAsync(path, ct).ConfigureAwait(false);
                }
                finally
                {
                    _queued.TryRemove(path, out _);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unerwarteter Fehler in der Verarbeitungsschleife.");
            }
        }
    }

    private async Task ProcessQueuedFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return;

        if (!await _stabilityChecker.WaitUntilStableAsync(path, ct).ConfigureAwait(false))
        {
            _logger.LogWarning("Datei {File} war nicht rechtzeitig vollstaendig geschrieben.", Path.GetFileName(path));
            return;
        }

        var result = await _processor.ProcessAsync(path, ct).ConfigureAwait(false);
        FileProcessed?.Invoke(this, result);
    }

    /// <summary>Zyklischer Nachscan als Sicherheitsnetz fuer verpasste Watcher-Events.</summary>
    private async Task RescanLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Processing.RescanIntervalSeconds, 10, 3600));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
                EnqueueExistingFiles();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nachscan des Eingangsordners fehlgeschlagen.");
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _signal.Dispose();
    }
}
