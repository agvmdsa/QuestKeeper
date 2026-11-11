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

    private static readonly string Ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");

    // Windows: ping.exe for 30 rounds prints slowly and runs far past the limit. Unix: sleep. (Windows branch runs on Windows CI / the owner's PC only.)
    [Fact]
    public async Task Stops_a_program_that_runs_too_long()
    {
        var (exe, args) = OperatingSystem.IsWindows() ? (Ping, new[] { "-n", "30", "127.0.0.1" }) : ("/bin/sleep", new[] { "30" });
        if (!File.Exists(exe))
        {
            return;
        }

        var started = DateTime.UtcNow;
        var result = await new TailscaleProcess().RunAsync(exe, args, TimeSpan.FromMilliseconds(300), 1024 * 1024, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.TimedOut);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    // Windows: ping -t never ends and prints a line a second; the limit is tiny. (Windows branch runs on Windows CI / the owner's PC only.)
    [Fact]
    public async Task Flags_output_over_the_limit()
    {
        var (exe, args) = OperatingSystem.IsWindows() ? (Ping, new[] { "-t", "127.0.0.1" }) : ("/usr/bin/yes", Array.Empty<string>());
        if (!File.Exists(exe))
        {
            return;
        }

        var result = await new TailscaleProcess().RunAsync(exe, args, TimeSpan.FromSeconds(10), 16, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task A_program_that_cannot_start_throws_for_the_service_to_catch()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new TailscaleProcess().RunAsync(Path.Combine(Path.GetTempPath(), "no-such-program-xyz"), [], TimeSpan.FromSeconds(1), 1024, CancellationToken.None));
    }

    // A program that reads input until it ends: it only finishes when the runner has closed its input. (Windows branch runs on Windows CI / the owner's PC only.)
    [Fact]
    public async Task A_program_that_reads_input_until_the_end_finishes_because_its_input_is_closed()
    {
        var (exe, args) = OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sort.exe"), Array.Empty<string>())
            : ("/bin/cat", Array.Empty<string>());
        if (!File.Exists(exe))
        {
            return;
        }

        var result = await new TailscaleProcess().RunAsync(exe, args, TimeSpan.FromSeconds(8), 1024, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
    }
}
