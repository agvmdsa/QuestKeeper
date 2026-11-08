using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Infrastructure.Elevation;
using Avalonia;
using Serilog;

namespace AqwCompanion.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = AppPaths.ForCurrentUser(AppInfo.ExeName, StartupArguments.TryGetDataRoot(args));

        Log.Logger = LoggingSetup.CreateLogger(paths, AppInfo.Version);
        Log.Information("Starting {SafeProduct} {SafeVersion}", AppInfo.ProductName, AppInfo.Version);

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
