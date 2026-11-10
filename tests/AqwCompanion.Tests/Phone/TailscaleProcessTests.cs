using AqwCompanion.App.Phone;

namespace AqwCompanion.Tests.Phone;

/// <summary>The real runner against harmless system programs; each test returns quietly where its program does not exist.</summary>
public sealed class TailscaleProcessTests
{
    private static readonly string Cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    [Fact]
    public async Task Captures_output_and_exit_code()
    {
        var (exe, args) = OperatingSystem.IsWindows() ? (Cmd, new[] { "/c", "echo", "hello" }) : ("/bin/echo", new[] { "hello" });
        if (!File.Exists(exe))
        {
            return;
        }

        var result = await new TailscaleProcess().RunAsync(exe, args, TimeSpan.FromSeconds(10), 1024, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output);
        Assert.False(result.TimedOut);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Stops_a_program_that_runs_too_long()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sleep"))
        {
            return;
        }

        var result = await new TailscaleProcess().RunAsync("/bin/sleep", ["30"], TimeSpan.FromMilliseconds(300), 1024, CancellationToken.None);

        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task Flags_output_over_the_limit()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/usr/bin/yes"))
        {
            return;
        }

        var result = await new TailscaleProcess().RunAsync("/usr/bin/yes", [], TimeSpan.FromSeconds(10), 1024, CancellationToken.None);

        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task A_program_that_cannot_start_throws_for_the_service_to_catch()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new TailscaleProcess().RunAsync(Path.Combine(Path.GetTempPath(), "no-such-program-xyz"), [], TimeSpan.FromSeconds(1), 1024, CancellationToken.None));
    }
}
