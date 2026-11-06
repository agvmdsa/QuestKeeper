using System.Runtime.CompilerServices;
using AqwCompanion.Core.Diagnostics;
using Serilog;

namespace AqwCompanion.App.Infrastructure;

public static class CrashHandler
{
    private static readonly ConditionalWeakTable<Exception, object> Reported = new();

    public static void Install(string crashDirectory, string appVersion)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Report(ex, crashDirectory, appVersion);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) => Report(e.Exception, crashDirectory, appVersion);
    }

    /// <summary>
    /// Logs and writes one crash report per exception instance. Best effort: never throws,
    /// so it cannot mask the exception being reported.
    /// </summary>
    public static void Report(Exception exception, string crashDirectory, string appVersion)
    {
        if (!Reported.TryAdd(exception, new object()))
        {
            return;
        }

        try
        {
            Log.Fatal(exception, "Unhandled exception");
            CrashReportWriter.Write(exception, crashDirectory, appVersion, DateTimeOffset.Now);
        }
#pragma warning disable CA1031 // Crash path: nothing may escape the reporter.
        catch (Exception writeError)
        {
            try
            {
                Log.Error(writeError, "Could not write crash report");
            }
            catch (Exception)
            {
                // Logging itself failed; there is nowhere left to report to.
            }
        }
#pragma warning restore CA1031
    }
}
