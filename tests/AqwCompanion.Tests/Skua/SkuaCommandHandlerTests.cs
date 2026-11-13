using System.Reflection;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaCommandHandlerTests
{
    // Never touched on disk: the catalog only sees the paths the fake listing returns.
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skualink-handler-test", "Scripts"));

    private DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private List<string> _files = ["Farm/Gold.cs", "Story/Intro.cs"];
    private Func<IEnumerable<string>>? _listFiles;

    private static string At(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string IdOf(string relative) => SkuaScriptCatalog.IdOf(relative);

    private void Wait(double seconds) => _now = _now.AddSeconds(seconds);

    private (SkuaCommandHandler Handler, FakeHost Host, SkuaControlState Control) NewHandler(FakeHost? host = null)
    {
        host ??= new FakeHost();
        var catalog = new SkuaScriptCatalog(Root, _ => _listFiles is { } list ? list() : _files.Select(At).ToList(), _ => false);
        var control = new SkuaControlState(() => _now);
        return (new SkuaCommandHandler(host, catalog, control, () => _now), host, control);
    }

    private static ResultMessage Fail(string reqId, string error) => new(reqId, false, error, null, null, null, false, false);

    private static ResultMessage Ok(string reqId, bool hasOptions = false) => new(reqId, true, null, null, null, null, hasOptions, false);

    // ---- listScripts ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListScripts_answers_the_scanned_scripts()
    {
        var (handler, _, _) = NewHandler();

        var result = await handler.HandleAsync(new ListScriptsCommand("r1"));

        Assert.Equal(
            new ResultMessage(
                "r1", true, null,
                [new SkuaScriptInfo(IdOf("Farm/Gold.cs"), "Gold", "Farm"), new SkuaScriptInfo(IdOf("Story/Intro.cs"), "Intro", "Story")],
                null, null, false, false),
            result);
    }

    [Fact]
    public async Task ListScripts_says_when_the_list_was_cut()
    {
        _files = Enumerable.Range(0, SkuaProtocol.MaxScripts + 1).Select(i => $"s{i:D5}.cs").ToList();
        var (handler, _, _) = NewHandler();

        var result = await handler.HandleAsync(new ListScriptsCommand("r1"));

        Assert.True(result.Ok);
        Assert.True(result.Truncated);
        Assert.Equal(SkuaProtocol.MaxScripts, result.Scripts!.Count);
    }

    [Fact]
    public async Task ListScripts_answers_an_error_when_the_folder_cannot_be_read()
    {
        _listFiles = () => throw new UnauthorizedAccessException(@"Access to the path 'C:\Users\Someone\AppData\Roaming\Skua\Scripts' is denied.");
        var (handler, _, _) = NewHandler();

        var result = await handler.HandleAsync(new ListScriptsCommand("r1"));

        Assert.Equal(Fail("r1", "could not list scripts"), result);
    }

    // ---- start ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Start_loads_the_resolved_path_then_starts_and_answers_ok()
    {
        var (handler, host, control) = NewHandler();
        await handler.HandleAsync(new ListScriptsCommand("r0"));

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Ok("r1"), result);
        Assert.Equal([$"load {At("Farm/Gold.cs")}", "start"], host.Calls);
        Assert.Equal("running", control.State);
    }

    [Fact]
    public async Task Start_with_an_id_before_any_listing_scans_first()
    {
        var (handler, host, _) = NewHandler();

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Story/Intro.cs")));

        Assert.Equal(Ok("r1"), result);
        Assert.Equal([$"load {At("Story/Intro.cs")}", "start"], host.Calls);
    }

    [Theory]
    [InlineData("0000000000000000")]
    [InlineData("../../Windows/evil.cs")]
    [InlineData(@"C:\Windows\evil.cs")]
    [InlineData("")]
    public async Task Start_with_an_unknown_id_is_refused_and_touches_nothing(string id)
    {
        var (handler, host, control) = NewHandler();

        var result = await handler.HandleAsync(new StartCommand("r1", id));

        Assert.Equal(Fail("r1", "unknown script id"), result);
        Assert.Empty(host.Calls);
        Assert.Equal("idle", control.State);
    }

    [Fact]
    public async Task Start_of_a_listed_script_whose_file_is_gone_is_not_found()
    {
        var host = new FakeHost { Exists = _ => false };
        var (handler, _, _) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Fail("r1", "not found"), result);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task Start_while_a_script_runs_never_loads_another_script()
    {
        var host = new FakeHost { Running = true };
        var (handler, _, _) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Fail("r1", "already running"), result);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task Start_while_settling_or_stopping_is_refused_with_the_state()
    {
        var (handler, host, control) = NewHandler();
        control.OnScriptRunningChanged(true);
        control.OnScriptRunningChanged(false);

        var settling = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Fail("r1", "settling"), settling);

        Wait(10);
        control.OnScriptRunningChanged(true);
        control.OnStopRequested();
        var stopping = await handler.HandleAsync(new StartCommand("r2", IdOf("Farm/Gold.cs")));

        Assert.Equal(Fail("r2", "stopping"), stopping);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task A_start_error_from_skua_becomes_the_answer_with_paths_reduced_and_the_state_stays_idle()
    {
        var host = new FakeHost
        {
            StartResult = () => Task.FromResult<Exception?>(
                new InvalidOperationException(@"Script compile error in C:\Users\Someone\AppData\Roaming\Skua\Scripts\Farm\Gold.cs: CS1002 ; expected")),
        };
        var (handler, _, control) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Fail("r1", "Script compile error in Gold.cs: CS1002 ; expected"), result);
        Assert.Equal("idle", control.State);
        Assert.False(host.OptionsRead);
    }

    [Fact]
    public async Task A_long_start_error_is_cut_to_the_text_cap()
    {
        var host = new FakeHost { StartResult = () => Task.FromResult<Exception?>(new Exception(new string('e', 5000))) };
        var (handler, _, _) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.False(result.Ok);
        Assert.Equal(SkuaProtocol.MaxTextLength, result.Error!.Length);
    }

    [Fact]
    public async Task A_start_that_throws_is_answered_and_never_escapes()
    {
        var host = new FakeHost { StartResult = () => throw new InvalidOperationException("boom") };
        var (handler, _, control) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Fail("r1", "boom"), result);
        Assert.Equal("idle", control.State);
    }

    [Fact]
    public async Task A_load_that_throws_is_answered_and_skua_is_not_started()
    {
        var host = new FakeHost { Load = _ => throw new InvalidOperationException("no load") };
        var (handler, _, _) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Fail("r1", "no load"), result);
        Assert.DoesNotContain("start", host.Calls);
    }

    [Fact]
    public async Task A_script_with_options_awaits_them_and_says_so()
    {
        var host = new FakeHost { HasOptions = true };
        var (handler, _, control) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Ok("r1", hasOptions: true), result);
        Assert.True(host.OptionsRead);
        Assert.Equal("awaitingOptions", control.State);
    }

    [Fact]
    public async Task Options_that_cannot_be_read_count_as_none()
    {
        var host = new FakeHost { OptionsThrow = true };
        var (handler, _, control) = NewHandler(host);

        var result = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Assert.Equal(Ok("r1"), result);
        Assert.Equal("running", control.State);
    }

    [Fact]
    public async Task Two_starts_at_once_are_handled_one_after_the_other_and_only_one_script_is_loaded()
    {
        var release = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost();
        host.StartResult = () =>
        {
            host.Running = true;
            return release.Task;
        };
        var (handler, _, _) = NewHandler(host);

        var first = handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));
        var second = handler.HandleAsync(new StartCommand("r2", IdOf("Story/Intro.cs")));
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        release.SetResult(null);

        Assert.Equal(Ok("r1"), await first);
        Assert.Equal(Fail("r2", "already running"), await second);
        Assert.Single(host.Calls, c => c.StartsWith("load ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_accepted_start_that_never_runs_settles_after_10_seconds()
    {
        var (handler, _, control) = NewHandler();
        await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));
        Assert.Equal("running", control.State);

        Wait(9);
        handler.Tick();
        Assert.Equal("running", control.State);

        Wait(1);
        handler.Tick();
        Assert.Equal("settling", control.State);

        Wait(10);
        handler.Tick();
        Assert.Equal("idle", control.State);
    }

    [Fact]
    public async Task An_accepted_start_with_options_that_never_runs_settles_too()
    {
        var host = new FakeHost { HasOptions = true };
        var (handler, _, control) = NewHandler(host);
        await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Wait(10);
        handler.Tick();

        Assert.Equal("settling", control.State);
    }

    [Fact]
    public async Task A_started_script_that_runs_stays_running_past_10_seconds()
    {
        var host = new FakeHost();
        host.StartResult = () =>
        {
            host.Running = true;
            return Task.FromResult<Exception?>(null);
        };
        var (handler, _, control) = NewHandler(host);
        await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        Wait(60);
        handler.Tick();

        Assert.Equal("running", control.State);
    }

    [Fact]
    public void Tick_feeds_skua_running_state_into_the_control_state()
    {
        var host = new FakeHost { Running = true };
        var (handler, _, control) = NewHandler(host);

        handler.Tick();
        Assert.Equal("running", control.State);

        host.Running = false;
        handler.Tick();
        Assert.Equal("settling", control.State);
    }

    [Fact]
    public void Tick_never_throws()
    {
        var host = new FakeHost { RunningThrows = true };
        var (handler, _, _) = NewHandler(host);

        handler.Tick();
    }

    // ---- stop -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stop_answers_at_once_while_skua_is_still_stopping_on_another_thread()
    {
        using var release = new ManualResetEventSlim();
        var host = new FakeHost
        {
            Running = true,
            StopResult = () =>
            {
                // Blocks the thread it is called on: an answer that arrives anyway was not waiting for it.
                release.Wait(TimeSpan.FromSeconds(10));
                return Task.CompletedTask;
            },
        };
        var (handler, _, control) = NewHandler(host);
        handler.Tick();

        var result = await handler.HandleAsync(new StopCommand("r1")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Ok("r1"), result);
        Assert.Equal("stopping", control.State);
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(handler.PendingStop.IsCompleted);
        release.Set();
        await handler.PendingStop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_second_stop_while_skua_is_still_stopping_does_not_stop_again()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost { Running = true, StopResult = () => stopped.Task };
        var (handler, _, _) = NewHandler(host);
        handler.Tick();

        await handler.HandleAsync(new StopCommand("r1"));
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await handler.HandleAsync(new StopCommand("r2"));

        Assert.Equal(Ok("r2"), second);
        Assert.Equal(1, host.StopCount);
        stopped.SetResult();
    }

    [Fact]
    public async Task A_stop_after_skua_gave_up_on_an_unresponsive_script_stops_again()
    {
        var host = new FakeHost { Running = true };
        var (handler, _, _) = NewHandler(host);
        handler.Tick();

        await handler.HandleAsync(new StopCommand("r1"));
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.PendingStop.WaitAsync(TimeSpan.FromSeconds(5));
        host.StopCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = await handler.HandleAsync(new StopCommand("r2"));
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Ok("r2"), second);
        Assert.Equal(2, host.StopCount);
    }

    [Fact]
    public async Task Stop_with_nothing_running_is_refused()
    {
        var (handler, host, control) = NewHandler();

        var result = await handler.HandleAsync(new StopCommand("r1"));

        Assert.Equal(Fail("r1", "not running"), result);
        Assert.Equal(0, host.StopCount);
        Assert.Equal("idle", control.State);
    }

    [Fact]
    public async Task Stop_of_an_accepted_start_skua_has_not_reported_yet_settles_without_calling_skua()
    {
        var (handler, host, control) = NewHandler();
        await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));

        var result = await handler.HandleAsync(new StopCommand("r2"));
        handler.Tick();

        Assert.Equal(Ok("r2"), result);
        Assert.Equal(0, host.StopCount);
        Assert.Equal("settling", control.State);
    }

    [Fact]
    public async Task A_stop_that_throws_in_skua_does_not_escape()
    {
        var host = new FakeHost { Running = true, StopResult = () => throw new InvalidOperationException("stop failed") };
        var (handler, _, _) = NewHandler(host);
        handler.Tick();

        var result = await handler.HandleAsync(new StopCommand("r1"));
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.PendingStop.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Ok("r1"), result);
    }

    // ---- every command ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Options_commands_are_not_supported_yet_and_still_answered()
    {
        var (handler, host, _) = NewHandler();

        var get = await handler.HandleAsync(new GetOptionsCommand("r1", IdOf("Farm/Gold.cs")));
        var set = await handler.HandleAsync(new SetOptionsCommand("r2", IdOf("Farm/Gold.cs"), [new SkuaOptionValue("Options", "A", "1")]));

        Assert.Equal(Fail("r1", "not supported"), get);
        Assert.Equal(Fail("r2", "not supported"), set);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task A_failing_host_is_answered_with_an_error_and_never_throws()
    {
        var host = new FakeHost { RunningThrows = true };
        var (handler, _, _) = NewHandler(host);

        var start = await handler.HandleAsync(new StartCommand("r1", IdOf("Farm/Gold.cs")));
        var stop = await handler.HandleAsync(new StopCommand("r2"));

        Assert.Equal("r1", start.ReqId);
        Assert.False(start.Ok);
        Assert.Equal("r2", stop.ReqId);
        Assert.False(stop.Ok);
    }

    [Fact]
    public async Task Each_answer_carries_the_request_id_of_its_command()
    {
        var host = new FakeHost();
        var (handler, _, _) = NewHandler(host);
        SkuaCommand[] commands =
        [
            new ListScriptsCommand("a"), new StartCommand("b", IdOf("Farm/Gold.cs")), new StartCommand("c", "nope"),
            new StopCommand("d"), new GetOptionsCommand("e", "x"), new SetOptionsCommand("f", "x", []),
        ];

        var results = await Task.WhenAll(commands.Select(handler.HandleAsync));

        Assert.Equal(["a", "b", "c", "d", "e", "f"], results.Select(r => r.ReqId));
    }

    private sealed class FakeHost : ISkuaScriptHost
    {
        private int _stopCount;

        public List<string> Calls { get; } = [];
        public bool Running { get; set; }
        public bool RunningThrows { get; set; }
        public bool HasOptions { get; set; }
        public bool OptionsThrow { get; set; }
        public bool OptionsRead { get; private set; }
        public Func<string, bool> Exists { get; set; } = _ => true;
        public Action<string> Load { get; set; } = _ => { };
        public Func<Task<Exception?>> StartResult { get; set; } = () => Task.FromResult<Exception?>(null);
        public Func<Task> StopResult { get; set; } = () => Task.CompletedTask;
        public TaskCompletionSource StopCalled { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCount => Volatile.Read(ref _stopCount);

        public bool ScriptRunning => RunningThrows ? throw new TargetInvocationException(new InvalidOperationException("gone")) : Running;

        public bool ScriptExists(string fullPath) => Exists(fullPath);

        public void SetLoadedScript(string fullPath)
        {
            lock (Calls)
            {
                Calls.Add("load " + fullPath);
            }

            Load(fullPath);
        }

        public Task<Exception?> StartScript()
        {
            lock (Calls)
            {
                Calls.Add("start");
            }

            return StartResult();
        }

        public bool LoadedScriptHasOptions()
        {
            OptionsRead = true;
            return OptionsThrow ? throw new InvalidOperationException("config") : HasOptions;
        }

        public Task StopScript()
        {
            Interlocked.Increment(ref _stopCount);
            StopCalled.TrySetResult();
            return StopResult();
        }
    }
}
