using AqwCompanion.App.Infrastructure;

namespace AqwCompanion.Tests.Shell;

public sealed class CrashHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));

    private string CrashDirectory => Path.Combine(_root, "crashes");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Reporting_the_same_exception_instance_twice_writes_one_report()
    {
        var exception = new InvalidOperationException("boom");

        CrashHandler.Report(exception, CrashDirectory, "1.0.0");
        Thread.Sleep(5);
        CrashHandler.Report(exception, CrashDirectory, "1.0.0");

        Assert.Single(Directory.GetFiles(CrashDirectory, "crash-*.txt"));
    }

    [Fact]
    public void Reporting_two_different_exceptions_writes_two_reports()
    {
        CrashHandler.Report(new InvalidOperationException("first"), CrashDirectory, "1.0.0");
        Thread.Sleep(5);
        CrashHandler.Report(new InvalidOperationException("second"), CrashDirectory, "1.0.0");

        Assert.Equal(2, Directory.GetFiles(CrashDirectory, "crash-*.txt").Length);
    }

    [Fact]
    public void Reporting_into_a_path_occupied_by_a_file_does_not_throw()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(CrashDirectory, "not a directory");

        var error = Record.Exception(() => CrashHandler.Report(new InvalidOperationException("boom"), CrashDirectory, "1.0.0"));

        Assert.Null(error);
    }

    [Fact]
    public void Reporting_with_an_invalid_crash_directory_does_not_throw()
    {
        var error = Record.Exception(() => CrashHandler.Report(new InvalidOperationException("boom"), string.Empty, "1.0.0"));

        Assert.Null(error);
    }
}
