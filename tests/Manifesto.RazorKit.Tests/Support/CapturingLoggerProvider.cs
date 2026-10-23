using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Manifesto.RazorKit.Tests.Support;

public sealed record CapturedLog(string Category, LogLevel Level, string Message, string State);

/// <summary>Keeps every formatted log entry, and the raw state, so tests can assert on what was logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var stateText = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"))
                : state?.ToString() ?? string.Empty;
            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception) + exception, stateText));
        }
    }
}
