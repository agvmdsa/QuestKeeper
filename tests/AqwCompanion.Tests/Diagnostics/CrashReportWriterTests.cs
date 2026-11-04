using AqwCompanion.Core.Diagnostics;

namespace AqwCompanion.Tests.Diagnostics;

public sealed class CrashReportWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Write_creates_a_timestamped_report_with_version_and_exception()
    {
        var now = new DateTimeOffset(2026, 11, 4, 13, 14, 15, 123, TimeSpan.Zero);

        var path = CrashReportWriter.Write(new InvalidOperationException("kaboom"), _dir, "2.0.0", now);

        Assert.Equal("crash-20261104-131415123.txt", Path.GetFileName(path));
        var text = File.ReadAllText(path);
        Assert.Contains("AppVersion: 2.0.0", text);
        Assert.Contains("kaboom", text);
        Assert.Contains("InvalidOperationException", text);
    }
}
