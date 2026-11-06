using AqwCompanion.App;
using AqwCompanion.App.Infrastructure;

namespace AqwCompanion.Tests.Shell;

public sealed class LoggingSetupTests : IDisposable
{
    private readonly AppPaths _paths =
        new(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_paths.Root))
        {
            Directory.Delete(_paths.Root, recursive: true);
        }
    }

    [Fact]
    public void Logger_writes_a_file_in_the_log_directory_with_the_app_version_on_each_line()
    {
        using (var logger = LoggingSetup.CreateLogger(_paths, "9.8.7-test"))
        {
            logger.Information("Starting {Product} {Version}", "AQW Companion", "1.2.3");
        }

        var file = Assert.Single(Directory.GetFiles(_paths.LogDirectory, "log-*.txt"));
        var text = File.ReadAllText(file);
        Assert.Contains("[INF] 9.8.7-test Starting AQW Companion 1.2.3", text);
    }
}
