using Avalonia;
using Avalonia.Headless;
using AqwCompanion.App;

[assembly: AvaloniaTestApplication(typeof(AqwCompanion.Tests.Desktop.TestAppBuilder))]

namespace AqwCompanion.Tests.Desktop;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<CompanionApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
