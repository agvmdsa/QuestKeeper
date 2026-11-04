using Avalonia;
using Serilog;

namespace AqwCompanion.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = AppPaths.ForCurrentUser(AppInfo.ExeName);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("AppVersion", AppInfo.Version)
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        CrashHandler.Install(paths.CrashDirectory, AppInfo.Version);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashHandler.Report(ex, paths.CrashDirectory, AppInfo.Version);
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<CompanionApp>().UsePlatformDetect().LogToTrace();
}
