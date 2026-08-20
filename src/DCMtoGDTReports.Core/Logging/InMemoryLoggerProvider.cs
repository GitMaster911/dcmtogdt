using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DCMtoGDTReports.Core.Logging;

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message)
{
    public override string ToString() => $"{Timestamp:HH:mm:ss} [{Level,-11}] {Message}";
}

/// <summary>
/// Logger-Provider, der die letzten Meldungen im Speicher haelt, damit die GUI sie anzeigen kann.
/// Personenbezogene Daten gehoeren nicht ins Log - die Meldungen der Anwendung enthalten daher
/// nur Dateinamen und technische Kennungen.
/// </summary>
public sealed class InMemoryLoggerProvider(int capacity = 1000) : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly int _capacity = Math.Clamp(capacity, 50, 100_000);

    public event EventHandler<LogEntry>? EntryAdded;

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(this, categoryName);

    public IReadOnlyList<LogEntry> GetEntries() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }

    private void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > _capacity && _entries.TryDequeue(out _)) { }
        EntryAdded?.Invoke(this, entry);
    }

    public void Dispose() => Clear();

    private sealed class InMemoryLogger(InMemoryLoggerProvider provider, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
                message += $" | {exception.GetType().Name}: {exception.Message}";

            provider.Add(new LogEntry(DateTimeOffset.Now, logLevel, category, message));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
