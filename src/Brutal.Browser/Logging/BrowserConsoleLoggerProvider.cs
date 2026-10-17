using System.Text;
using Microsoft.Extensions.Logging;

namespace Brutal.Browser.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that writes to <see cref="Console.Out"/> / <see cref="Console.Error"/>, which the .NET
/// browser runtime forwards to <c>console.log</c> / <c>console.error</c>. Unlike the stock console provider it is
/// supported on <c>browser-wasm</c>. Trace..Warning go to stdout, Error and Critical to stderr.
/// </summary>
public sealed class BrowserConsoleLoggerProvider : ILoggerProvider
{
    private readonly LogLevel _minimumLevel;

    /// <summary>Creates the provider.</summary>
    /// <param name="minimumLevel">Messages below this level are dropped by the loggers of this provider.</param>
    public BrowserConsoleLoggerProvider(LogLevel minimumLevel = LogLevel.Trace) => _minimumLevel = minimumLevel;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _minimumLevel);

    /// <inheritdoc />
    public void Dispose() { }

    private sealed class Logger(string category, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var text = new StringBuilder(96)
                .Append('[').Append(Short(logLevel)).Append("] ").Append(category).Append(": ")
                .Append(formatter(state, exception));
            if (exception is not null)
                text.Append('\n').Append(exception);

            var writer = logLevel >= LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine(text.ToString());
        }

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none",
        };
    }
}
