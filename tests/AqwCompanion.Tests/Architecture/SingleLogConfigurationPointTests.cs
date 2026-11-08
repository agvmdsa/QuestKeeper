namespace AqwCompanion.Tests.Architecture;

public class SingleLogConfigurationPointTests
{
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AqwCompanion.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("AqwCompanion.slnx not found above the test folder.");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var relative = Path.GetRelativePath(Path.Combine(FindRepositoryRoot(), "src"), f);
                return !relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin");
            });

    private static bool IsLoggingSetup(string file) =>
        string.Equals(Path.GetFileName(file), "LoggingSetup.cs", StringComparison.Ordinal);

    [Fact]
    public void Logger_configuration_is_created_only_in_LoggingSetup()
    {
        var files = SourceFiles().ToList();
        Assert.Contains(files, IsLoggingSetup);

        var offenders = files
            .Where(f => !IsLoggingSetup(f) && File.ReadAllText(f).Contains("new LoggerConfiguration(", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_log_destination_is_added_outside_LoggingSetup()
    {
        var offenders = SourceFiles()
            .Where(f => !IsLoggingSetup(f) && File.ReadAllText(f).Contains(".WriteTo.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }
}
