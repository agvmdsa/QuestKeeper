using AqwCompanion.App.GameData;
using AqwCompanion.App.Infrastructure.Elevation;
using AqwCompanion.App.Shell;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace AqwCompanion.App;

public partial class CompanionApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = CompositionRoot.Build(AppPaths.ForCurrentUser(AppInfo.ExeName, StartupArguments.TryGetDataRoot(desktop.Args ?? [])));
            var main = services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = main };
            _ = main.Account.RestoreStoredDataAsync();
            _ = main.HardFarms.RestoreAsync();
            _ = main.Reputation.RestoreAsync();
            var capture = services.GetRequiredService<CaptureSession>();
            _ = capture.RestoreAsync();
            _ = capture.StartAsync();
            desktop.Exit += (_, _) => services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
