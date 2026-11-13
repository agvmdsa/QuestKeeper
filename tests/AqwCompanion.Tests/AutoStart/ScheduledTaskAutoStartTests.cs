using AqwCompanion.App.Infrastructure.AutoStart;

namespace AqwCompanion.Tests.AutoStart;

public class ScheduledTaskAutoStartTests
{
    private sealed class FakeRunner(params int[] exitCodes) : IProcessRunner
    {
        private readonly Queue<int> _codes = new(exitCodes);

        public List<(string File, string[] Args)> Calls { get; } = [];

        public int Run(string file, IReadOnlyList<string> args)
        {
            Calls.Add((file, [.. args]));
            return _codes.Count > 0 ? _codes.Dequeue() : 0;
        }
    }

    private const string Exe = @"C:\Program Files\SkuaLink\AqwCompanion.exe";

    private static ScheduledTaskAutoStart Build(FakeRunner runner, bool windows = true, string? exe = Exe) =>
        new(runner, exe, @"PC\player", () => windows);

    [Fact]
    public void It_is_supported_only_on_windows_with_a_known_exe()
    {
        Assert.True(Build(new FakeRunner()).IsSupported);
        Assert.False(Build(new FakeRunner(), windows: false).IsSupported);
        Assert.False(Build(new FakeRunner(), exe: null).IsSupported);
    }

    [Fact]
    public void Unsupported_never_runs_anything()
    {
        var runner = new FakeRunner();
        var autoStart = Build(runner, windows: false);

        Assert.False(autoStart.IsEnabled());
        Assert.False(autoStart.TrySet(true));
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void It_is_enabled_when_the_task_exists(int exitCode, bool expected)
    {
        var runner = new FakeRunner(exitCode);

        Assert.Equal(expected, Build(runner).IsEnabled());

        var call = Assert.Single(runner.Calls);
        Assert.Equal("schtasks.exe", call.File);
        Assert.Equal(["/Query", "/TN", ScheduledTaskAutoStart.TaskName], call.Args);
    }

    [Fact]
    public void Turning_it_on_creates_a_logon_task_that_runs_elevated_in_the_background()
    {
        var runner = new FakeRunner(0);

        Assert.True(Build(runner).TrySet(true));

        var call = Assert.Single(runner.Calls);
        Assert.Equal("schtasks.exe", call.File);
        Assert.Equal(
            [
                "/Create", "/TN", ScheduledTaskAutoStart.TaskName, "/TR", "\"" + Exe + "\" --background", "/SC", "ONLOGON",
                "/RL", "HIGHEST", "/RU", @"PC\player", "/IT", "/DELAY", "0000:15", "/F",
            ],
            call.Args);
    }

    [Fact]
    public void A_failed_create_is_reported()
    {
        Assert.False(Build(new FakeRunner(1)).TrySet(true));
    }

    [Fact]
    public void Turning_it_off_deletes_the_task_when_it_exists()
    {
        var runner = new FakeRunner(0, 0);

        Assert.True(Build(runner).TrySet(false));

        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(["/Delete", "/TN", ScheduledTaskAutoStart.TaskName, "/F"], runner.Calls[1].Args);
    }

    [Fact]
    public void Turning_it_off_when_there_is_no_task_succeeds_without_deleting()
    {
        var runner = new FakeRunner(1);

        Assert.True(Build(runner).TrySet(false));

        Assert.Single(runner.Calls);
    }

    [Fact]
    public void A_failed_delete_is_reported()
    {
        Assert.False(Build(new FakeRunner(0, 1)).TrySet(false));
    }
}
