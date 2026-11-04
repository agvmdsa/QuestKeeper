using AqwCompanion.Core.Diagnostics;
using Serilog;

namespace AqwCompanion.App;

public static class CrashHandler
{
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

    public static void Report(Exception exception, string crashDirectory, string appVersion)
    {
        Log.Fatal(exception, "Unhandled exception");
        try
        {
            CrashReportWriter.Write(exception, crashDirectory, appVersion, DateTimeOffset.Now);
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
            Log.Error(writeError, "Could not write crash report");
        }
    }
}
