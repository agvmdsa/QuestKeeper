using AqwCompanion.App.ViewModels;
using AqwCompanion.App.Views;
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
            var services = CompositionRoot.Build(AppPaths.ForCurrentUser(AppInfo.ExeName));
            desktop.MainWindow = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };
            desktop.Exit += (_, _) => services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
