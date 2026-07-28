using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Palmap.UnitTests;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<RecordedLogEntry> _entries = new();

    public IReadOnlyList<RecordedLogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => NoOpScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue(new(logLevel, formatter(state, exception), exception));

    private sealed class NoOpScope : IDisposable
    {
        public static NoOpScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record RecordedLogEntry(
    LogLevel Level,
    string Message,
    Exception? Exception);
