using Brutal.Logging;
using Microsoft.Extensions.Logging;

namespace Brutal.Browser.Logging;

/// <summary>Plugs <see cref="BrowserConsoleLoggerProvider"/> into BRUTAL's <see cref="LogSystem"/>.</summary>
public static class BrowserLogging
{
    /// <summary>
    /// Initializes BRUTAL's <see cref="LogSystem"/> with the browser console provider (replacing any previous factory).
    /// BRUTAL's own <c>AddConsole()</c> route throws <see cref="PlatformNotSupportedException"/> on browser-wasm, which is
    /// why this provider exists. <see cref="LogSystem.Initialize"/> accepts any <see cref="ILoggingBuilder"/> callback,
    /// so no limitation applies.
    /// </summary>
    public static void Install(LogLevel minimumLevel = LogLevel.Information)
    {
        LogSystem.Initialize(builder => builder
            .SetMinimumLevel(minimumLevel)
            .AddProvider(new BrowserConsoleLoggerProvider(minimumLevel)));
    }

    /// <summary>Installs the provider only when the application has not initialized <see cref="LogSystem"/> itself.</summary>
    public static void InstallIfUnconfigured(LogLevel minimumLevel = LogLevel.Information)
    {
        if (!LogSystem.IsEnabled)
            Install(minimumLevel);
    }
}
