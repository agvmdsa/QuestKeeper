using AqwCompanion.App.Feed;
using AqwCompanion.App.GameData;
using AqwCompanion.App.Infrastructure.Elevation;
using AqwCompanion.App.Phone;
using AqwCompanion.App.Shell;
using AqwCompanion.App.Skua;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.App;

public partial class CompanionApp : Application
{
    private TrayController? _tray;

    /// <summary>Brings the main window to the front; used when a second launch signals this running app. UI thread only.</summary>
    public void OpenMainWindow() => _tray?.Open();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = CompositionRoot.Build(AppPaths.ForCurrentUser(AppInfo.ExeName, StartupArguments.TryGetDataRoot(desktop.Args ?? [])));
            var main = services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = main };
            // Started by the sign-in task, the app waits in the notification area; its window opens from the tray icon.
            if (!StartupArguments.IsBackground(desktop.Args ?? []))
            {
                desktop.MainWindow = window;
            }

            // Closing the window hides it to the notification area; only Exit (or an OS shutdown) ends the app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            services.GetRequiredService<AvaloniaMainWindowHost>().Attach(window);
            var tray = services.GetRequiredService<TrayController>();
            window.KeepInTray(tray);
            _tray = tray;
            var trayIcon = new TrayIconSetup(tray);
            TrayIcon.SetIcons(this, [trayIcon.Icon]);
            _ = main.HardFarms.RestoreAsync();
            main.Debug.Start();
            services.GetRequiredService<LocalFeedController>().Start();
            services.GetRequiredService<PhoneController>().Start();
            services.GetRequiredService<SkuaBridgeHost>().Start();
            var capture = services.GetRequiredService<CaptureSession>();
            _ = capture.RestoreAsync();
            _ = capture.StartAsync();
            desktop.Exit += (_, _) =>
            {
                trayIcon.Dispose();
                services.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
