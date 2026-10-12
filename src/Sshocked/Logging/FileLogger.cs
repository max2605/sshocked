using Microsoft.Extensions.Logging;
using Sshocked.Services;

namespace Sshocked.Logging;

public sealed class FileLogger(string categoryName, FileLoggerProcessor processor) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (exception is not null)
        {
            message = $"{message}{Environment.NewLine}{exception}";
        }

        processor.Enqueue(new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            LogLevel = logLevel,
            CategoryName = categoryName,
            Message = AuditService.Redact(message) ?? string.Empty
        });
    }
}
