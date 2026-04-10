using Microsoft.Extensions.Logging;
using Xunit;

namespace ZibStack.NET.Log.Tests;

public class LoggerStructuredExtensionsTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public void LogInformation_InterpolatedString_CapturesTemplate()
    {
        var user = "Alice";
        var count = 5;

        _logger.LogInformation($"User {user} bought {count} items");

        Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, _logger.Entries[0].Level);
        Assert.Equal("User {user} bought {count} items", _logger.Entries[0].Template);
        Assert.Equal(new object?[] { "Alice", 5 }, _logger.Entries[0].Args);
    }

    [Fact]
    public void LogWarning_InterpolatedString_CapturesTemplate()
    {
        var id = 42;
        _logger.LogWarning($"Order {id} not found");

        Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, _logger.Entries[0].Level);
        Assert.Equal("Order {id} not found", _logger.Entries[0].Template);
        Assert.Equal(new object?[] { 42 }, _logger.Entries[0].Args);
    }

    [Fact]
    public void LogError_WithException_CapturesAll()
    {
        var ex = new InvalidOperationException("boom");
        var step = "validation";

        _logger.LogError(ex, $"Failed at {step}");

        Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, _logger.Entries[0].Level);
        Assert.Equal("Failed at {step}", _logger.Entries[0].Template);
        Assert.Equal(new object?[] { "validation" }, _logger.Entries[0].Args);
        Assert.Same(ex, _logger.Entries[0].Exception);
    }

    [Fact]
    public void LogDebug_FormatSpecifier_PreservedInTemplate()
    {
        var total = 29.97m;
        _logger.LogDebug($"Total: {total:C}");

        Assert.Single(_logger.Entries);
        Assert.Equal("Total: {total:C}", _logger.Entries[0].Template);
        Assert.Equal(new object?[] { 29.97m }, _logger.Entries[0].Args);
    }

    [Fact]
    public void LogTrace_ComplexExpression_SanitizedName()
    {
        var order = new { Name = "Widget" };
        _logger.LogTrace($"Product: {order.Name}");

        Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Trace, _logger.Entries[0].Level);
        // "order.Name" → "orderName" via SanitizeName
        Assert.Equal("Product: {orderName}", _logger.Entries[0].Template);
        Assert.Equal(new object?[] { "Widget" }, _logger.Entries[0].Args);
    }

    [Fact]
    public void LogCritical_NoInterpolation_StillWorks()
    {
        _logger.LogCritical($"System shutting down");

        Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Critical, _logger.Entries[0].Level);
        Assert.Equal("System shutting down", _logger.Entries[0].Template);
        Assert.Empty(_logger.Entries[0].Args);
    }

    [Fact]
    public void LogInformation_BracesInLiteral_EscapedInTemplate()
    {
        var x = 1;
        _logger.LogInformation($"Result: {x} in {{braces}}");

        Assert.Single(_logger.Entries);
        // The literal "Result: " and " in {braces}" go through AppendLiteral
        // which escapes { → {{ and } → }}
        // The {x} is an interpolation → {x}
        // C# parses $"Result: {x} in {{braces}}" as:
        //   AppendLiteral("Result: "), AppendFormatted(x), AppendLiteral(" in {braces}")
        // The literal " in {braces}" gets escaped to " in {{braces}}"
        Assert.Equal("Result: {x} in {{braces}}", _logger.Entries[0].Template);
    }

    [Fact]
    public void DisabledLevel_SkipsLog()
    {
        _logger.MinLevel = LogLevel.Warning;

        var name = "test";
        _logger.LogInformation($"Hello {name}");

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void AllLevels_Work()
    {
        var x = 1;
        _logger.LogTrace($"trace {x}");
        _logger.LogDebug($"debug {x}");
        _logger.LogInformation($"info {x}");
        _logger.LogWarning($"warn {x}");
        _logger.LogError($"error {x}");
        _logger.LogCritical($"critical {x}");

        Assert.Equal(6, _logger.Entries.Count);
        Assert.Equal(LogLevel.Trace, _logger.Entries[0].Level);
        Assert.Equal(LogLevel.Debug, _logger.Entries[1].Level);
        Assert.Equal(LogLevel.Information, _logger.Entries[2].Level);
        Assert.Equal(LogLevel.Warning, _logger.Entries[3].Level);
        Assert.Equal(LogLevel.Error, _logger.Entries[4].Level);
        Assert.Equal(LogLevel.Critical, _logger.Entries[5].Level);
    }

    // ── Test infrastructure ─────────────────────────────────────────────

    private sealed class TestLogger : ILogger
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Trace;
        public List<LogEntry> Entries { get; } = new();

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Extract the template and args from the FormattedLogValues state
            // Microsoft.Extensions.Logging.Log(level, template, args) creates FormattedLogValues internally
            string? template = null;
            object?[] args = Array.Empty<object?>();

            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                // Last entry is {OriginalFormat} = the template
                foreach (var kvp in values)
                {
                    if (kvp.Key == "{OriginalFormat}")
                    {
                        template = kvp.Value?.ToString();
                        break;
                    }
                }

                // All entries except {OriginalFormat} are the args
                args = values
                    .Where(v => v.Key != "{OriginalFormat}")
                    .Select(v => v.Value)
                    .ToArray();
            }

            Entries.Add(new LogEntry(logLevel, template ?? "", args, exception));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public record LogEntry(LogLevel Level, string Template, object?[] Args, Exception? Exception = null);
    }
}
