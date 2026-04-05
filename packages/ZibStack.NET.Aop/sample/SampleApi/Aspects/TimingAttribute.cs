using ZibStack.NET.Aop;
using ZibStack.NET.Aop.Aspects;

namespace ZibStack.NET.Aop.Sample.Aspects;

/// <summary>
/// Custom timing recorder that uses ILogger via DI.
/// No more static events needed!
/// </summary>
public class LoggingTimingRecorder : ITimingRecorder
{
    private readonly ILogger<LoggingTimingRecorder> _logger;

    public LoggingTimingRecorder(ILogger<LoggingTimingRecorder> logger) => _logger = logger;

    public void Record(string className, string methodName, long elapsedMilliseconds)
    {
        _logger.LogInformation("[Timing] {Class}.{Method} completed in {Ms}ms",
            className, methodName, elapsedMilliseconds);
    }
}
