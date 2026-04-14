using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log.Tests.Fixtures;

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, CapturingLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, _ => new CapturingLogger());

    public List<LogEntry> AllEntries =>
        _loggers.Values.SelectMany(l => l.Entries).ToList();

    public void Clear()
    {
        foreach (var logger in _loggers.Values)
            logger.Entries.Clear();
    }

    public void Dispose() { }
}

public sealed class CapturingLogger : ILogger
{
    public List<LogEntry> Entries { get; } = new();

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}

public record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
