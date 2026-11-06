using Serilog;
using Serilog.Core;

namespace AqwCompanion.App.Infrastructure;

public static class LoggingSetup
{
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {AppVersion} {Message:lj}{NewLine}{Exception}";

    public static Logger CreateLogger(AppPaths paths, string appVersion) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("AppVersion", appVersion)
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "log-.txt"),
                outputTemplate: OutputTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
}
