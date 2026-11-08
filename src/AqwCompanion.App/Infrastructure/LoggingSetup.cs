using AqwCompanion.App.Infrastructure.Logging;
using Serilog;
using Serilog.Core;

namespace AqwCompanion.App.Infrastructure;

/// <summary>The single place that builds the logger. Every event passes the <see cref="PrivacyLogSink"/> before the file.</summary>
public static class LoggingSetup
{
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {AppVersion} {Message:lj}{NewLine}{ExceptionSummary}{NewLine}";

    public static Logger CreateLogger(AppPaths paths, string appVersion)
    {
        var fileLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "log-.txt"),
                outputTemplate: OutputTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("AppVersion", appVersion)
            .WriteTo.Sink(new PrivacyLogSink(fileLogger))
            .CreateLogger();
    }
}
