using Avalonia;
using Avalonia.Headless;
using AqwCompanion.App;

[assembly: AvaloniaTestApplication(typeof(AqwCompanion.Tests.Shell.TestAppBuilder))]

namespace AqwCompanion.Tests.Shell;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<CompanionApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
