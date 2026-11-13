using System.Globalization;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

/// <summary>getOptions, setOptions and the options dialog after a start (AQW-0089), with a fake Skua and a fake clock.</summary>
public class SkuaCommandHandlerOptionsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skualink-handler-options-test", "Scripts"));
    private static readonly string OptionsRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "skualink-handler-options-test", "options"));
    private static readonly string ButlerFile = Path.Combine(OptionsRoot, "Butler.cfg");

    private readonly object _clock = new();
    private DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    public enum Mode
    {
        Fast,
        Very_Slow,
    }

    private DateTimeOffset Now()
    {
        lock (_clock)
        {
            return _now;
        }
    }

    private void Wait(double seconds)
    {
        lock (_clock)
        {
            _now = _now.AddSeconds(seconds);
        }
    }

    /// <summary>The handler's waits pass on the fake clock at once.</summary>
    private Task Delay(TimeSpan time)
    {
        Wait(time.TotalSeconds);
        return Task.CompletedTask;
    }

    private static string At(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string IdOf(string relative) => SkuaScriptCatalog.IdOf(relative);

    private static readonly string Butler = IdOf("Tools/Butler.cs");
    private static readonly string Plain = IdOf("Farm/Gold.cs");

    private static IReadOnlyList<SkuaOptionDescriptor> ButlerOptions =>
    [
        new("Options", "Follow", "Player to follow", "Who to follow", typeof(string), ""),
        new("Options", "Loop", "Loop", "", typeof(bool), true),
        new("Options", "Speed", "Speed", "", typeof(Mode), Mode.Fast),
        new("Options", "Count", "Count", "", typeof(int), 5),
    ];

    private static IReadOnlyList<SkuaOptionDef> ButlerDefs => SkuaScriptOptions.Definitions(ButlerOptions);

    private (SkuaCommandHandler Handler, FakeHost Host, FakeOptions Options, SkuaControlState Control) NewHandler()
    {
        var host = new FakeHost();
        var options = new FakeOptions(host);
        var catalog = new SkuaScriptCatalog(Root, _ => new[] { "Tools/Butler.cs", "Farm/Gold.cs" }.Select(At).ToList(), _ => false);
        var control = new SkuaControlState(Now);
        host.Clock = Now;
        options.Clock = Now;
        host.OptionsFor = path => path == At("Tools/Butler.cs") ? new SkuaLoadedOptions(path, ButlerFile, ButlerOptions) : new SkuaLoadedOptions(path, Path.Combine(OptionsRoot, "default.cfg"), []);
        return (new SkuaCommandHandler(host, catalog, control, Now, options, Delay), host, options, control);
    }

    /// <summary>Starts Butler once with a dialog that closes on the first try, so its options are known; then lets it stop and settle.</summary>
    private async Task Discover(SkuaCommandHandler handler, FakeHost host, FakeOptions options, SkuaControlState control)
    {
        options.Window = options.ClosableDialog();
        var first = await handler.HandleAsync(new StartCommand("discover", Butler));
        Assert.True(first.FirstRun);
        await handler.PendingStop.WaitAsync(TimeSpan.FromSeconds(5));
        host.Running = false;
        handler.Tick();
        Wait(10);
        handler.Tick();
        Assert.Equal("idle", control.State);
        options.Window = () => SkuaOptionsWindow.None;
        options.Closes.Clear();
    }

    // ---- getOptions -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetOptions_for_an_unknown_id_is_refused()
    {
        var (handler, _, options, _) = NewHandler();

        var result = await handler.HandleAsync(new GetOptionsCommand("r1", "0000000000000000"));

        Assert.Equal(new ResultMessage("r1", false, "unknown script id", null, null, null, false, false), result);
        Assert.Empty(options.Reads);
    }

    [Fact]
    public async Task GetOptions_for_a_script_not_started_yet_says_the_options_are_not_known()
    {
        var (handler, _, options, _) = NewHandler();

        var result = await handler.HandleAsync(new GetOptionsCommand("r1", Butler));

        Assert.Equal(new ResultMessage("r1", true, null, null, null, null, false, false), result);
        Assert.Empty(options.Reads);
    }

    [Fact]
    public async Task GetOptions_after_a_start_answers_the_definitions_and_the_saved_values()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        options.Files[ButlerFile] = ["Options:Follow=Artix", "Options:Speed=Very_Slow", "Options:Gone=1", "Options:Loop=False"];

        var result = await handler.HandleAsync(new GetOptionsCommand("r1", Butler));

        Assert.Equal(
            new ResultMessage(
                "r1", true, null, null, ButlerDefs,
                [new("Options", "Follow", "Artix"), new("Options", "Loop", "False"), new("Options", "Speed", "Very Slow")],
                true, false),
            result);
    }

    [Fact]
    public async Task GetOptions_without_an_options_file_answers_no_saved_values()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);

        var result = await handler.HandleAsync(new GetOptionsCommand("r1", Butler));

        Assert.True(result.Ok);
        Assert.Equal(ButlerDefs, result.OptionDefs);
        Assert.Equal([], result.Values);
    }

    [Fact]
    public async Task GetOptions_that_cannot_read_the_file_answers_an_error()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        options.ReadThrows = true;

        var result = await handler.HandleAsync(new GetOptionsCommand("r1", Butler));

        Assert.Equal(new ResultMessage("r1", false, "could not read options", null, null, null, false, false), result);
    }

    [Fact]
    public async Task GetOptions_for_a_script_without_options_answers_an_empty_list()
    {
        var (handler, host, _, _) = NewHandler();
        await handler.HandleAsync(new StartCommand("r0", Plain));
        host.Running = false;

        var result = await handler.HandleAsync(new GetOptionsCommand("r1", Plain));

        Assert.Equal(new ResultMessage("r1", true, null, null, [], [], false, false), result);
    }

    // ---- setOptions -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SetOptions_writes_the_merged_values_in_skua_format_to_the_script_options_file_only()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        options.Files[ButlerFile] = ["Options:Loop=False", "Options:Count=9", "Options:Gone=1"];

        var result = await handler.HandleAsync(new SetOptionsCommand("r1", Butler, [new("Options", "Follow", "Artix"), new("Options", "Speed", "Very Slow")]));

        IReadOnlyList<SkuaOptionValue> expected =
            [new("Options", "Follow", "Artix"), new("Options", "Loop", "False"), new("Options", "Speed", "Very Slow"), new("Options", "Count", "9")];
        Assert.Equal(new ResultMessage("r1", true, null, null, ButlerDefs, expected, true, false), result);
        var write = Assert.Single(options.Writes);
        Assert.Equal(ButlerFile, write.Path);
        Assert.Equal(["Options:Follow=Artix", "Options:Loop=False", "Options:Speed=Very Slow", "Options:Count=9"], write.Lines);
    }

    [Fact]
    public async Task Doubles_travel_invariant_and_are_written_and_read_in_the_pc_culture_skua_uses()
    {
        var (handler, host, options, control) = NewHandler();
        host.OptionsFor = path => new SkuaLoadedOptions(path, ButlerFile, [new("Options", "Ratio", "Ratio", "", typeof(double), 0.5)]);
        await Discover(handler, host, options, control);
        options.FileCulture = new CultureInfo("de-DE");

        var set = await handler.HandleAsync(new SetOptionsCommand("r1", Butler, [new("Options", "Ratio", "1.25")]));
        var get = await handler.HandleAsync(new GetOptionsCommand("r2", Butler));

        Assert.Equal(["Options:Ratio=1,25"], Assert.Single(options.Writes).Lines);
        Assert.Equal([new SkuaOptionValue("Options", "Ratio", "1.25")], set.Values);
        Assert.Equal([new SkuaOptionValue("Options", "Ratio", "1.25")], get.Values);
        Assert.Equal("0.5", Assert.Single(get.OptionDefs!).Default);
    }

    [Theory]
    [InlineData("Options", "Nope", "1", "unknown option Nope")]
    [InlineData("Options", "Count", "many", "not a valid int for Count")]
    [InlineData("Options", "Loop", "yes", "not a valid bool for Loop")]
    [InlineData("Options", "Speed", "Very_Slow", "not a valid enum for Speed")]
    [InlineData("Other", "Follow", "x", "unknown option Other:Follow")]
    [InlineData("Options", "Follow", "a\nb", "line break in Follow")]
    public async Task SetOptions_with_a_value_that_does_not_fit_is_refused_and_writes_nothing(string category, string name, string value, string error)
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);

        var result = await handler.HandleAsync(new SetOptionsCommand("r1", Butler, [new("Options", "Follow", "Artix"), new(category, name, value)]));

        Assert.Equal(new ResultMessage("r1", false, error, null, null, null, false, false), result);
        Assert.Empty(options.Writes);
    }

    [Fact]
    public async Task SetOptions_before_the_options_are_known_is_refused()
    {
        var (handler, _, options, _) = NewHandler();

        var result = await handler.HandleAsync(new SetOptionsCommand("r1", Butler, [new("Options", "Follow", "Artix")]));

        Assert.Equal(new ResultMessage("r1", false, "options not known yet", null, null, null, false, false), result);
        Assert.Empty(options.Writes);
    }

    [Fact]
    public async Task SetOptions_for_an_unknown_id_is_refused()
    {
        var (handler, _, options, _) = NewHandler();

        var result = await handler.HandleAsync(new SetOptionsCommand("r1", "../x.cs", []));

        Assert.Equal(new ResultMessage("r1", false, "unknown script id", null, null, null, false, false), result);
        Assert.Empty(options.Writes);
    }

    [Fact]
    public async Task SetOptions_while_a_script_runs_is_refused()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        host.Running = true;
        handler.Tick();

        var result = await handler.HandleAsync(new SetOptionsCommand("r1", Butler, [new("Options", "Follow", "Artix")]));

        Assert.Equal(new ResultMessage("r1", false, "already running", null, null, null, false, false), result);
        Assert.Empty(options.Writes);
    }

    [Fact]
    public async Task SetOptions_while_settling_is_allowed()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        host.Running = true;
        handler.Tick();
        host.Running = false;
        handler.Tick();
        Assert.Equal("settling", control.State);

        var result = await handler.HandleAsync(new SetOptionsCommand("r1", Butler, [new("Options", "Follow", "Artix")]));

        Assert.True(result.Ok);
        Assert.Single(options.Writes);
    }

    [Fact]
    public async Task SetOptions_never_writes_outside_skua_options_folder()
    {
        var (handler, host, options, control) = NewHandler();
        var outside = Path.Combine(OptionsRoot, "..", "Scripts", "Tools", "Butler.cs");
        host.OptionsFor = path => new SkuaLoadedOptions(path, outside, ButlerOptions);
        options.Window = options.ClosableDialog();
        await handler.HandleAsync(new StartCommand("r0", Butler));
        await handler.PendingStop.WaitAsync(TimeSpan.FromSeconds(5));
        host.Running = false;
        handler.Tick();
        Wait(10);
        handler.Tick();

        var get = await handler.HandleAsync(new GetOptionsCommand("r1", Butler));
        var set = await handler.HandleAsync(new SetOptionsCommand("r2", Butler, [new("Options", "Follow", "Artix")]));

        Assert.Equal([], get.Values);
        Assert.Empty(options.Reads);
        Assert.Equal(new ResultMessage("r2", false, "options file not allowed", null, null, null, false, false), set);
        Assert.Empty(options.Writes);
    }

    [Fact]
    public async Task SetOptions_that_cannot_write_answers_an_error()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        options.WriteThrows = true;

        var result = await handler.HandleAsync(new SetOptionsCommand("r1", Butler, [new("Options", "Follow", "Artix")]));

        Assert.Equal(new ResultMessage("r1", false, "could not write options", null, null, null, false, false), result);
    }

    // ---- first start of a script (discovery) ------------------------------------------------------------------------

    [Fact]
    public async Task The_first_start_captures_the_definitions_closes_the_dialog_and_stops_the_script_at_once()
    {
        var (handler, host, options, control) = NewHandler();
        options.Files[ButlerFile] = ["Options:Follow=Artix"];
        options.Window = options.ClosableDialog(appearsAfterSeconds: 1.5);

        var result = await handler.HandleAsync(new StartCommand("r1", Butler));

        Assert.Equal(new ResultMessage("r1", true, null, null, ButlerDefs, [new("Options", "Follow", "Artix")], true, true), result);
        Assert.Single(options.Closes);
        Assert.Equal("stopping", control.State);
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, host.StopCount);
        Assert.True(options.Closes[0] < host.StopAt, "the dialog is closed before the script is stopped");
    }

    [Fact]
    public async Task A_first_start_that_shows_no_dialog_still_stops_after_5_seconds()
    {
        var (handler, host, options, control) = NewHandler();
        var started = Now();

        var result = await handler.HandleAsync(new StartCommand("r1", Butler));

        Assert.True(result.Ok);
        Assert.True(result.FirstRun);
        Assert.Equal(ButlerDefs, result.OptionDefs);
        Assert.True(Now() - started >= TimeSpan.FromSeconds(5));
        Assert.True(Now() - started < TimeSpan.FromSeconds(6));
        Assert.Empty(options.Closes);
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("stopping", control.State);
    }

    [Fact]
    public async Task A_first_start_whose_dialog_cannot_be_closed_waits_for_the_pc_loudly_and_does_not_stop()
    {
        var (handler, host, options, control) = NewHandler();
        options.Window = () => SkuaOptionsWindow.Open;

        var result = await handler.HandleAsync(new StartCommand("r1", Butler));

        Assert.Equal(new ResultMessage("r1", true, null, null, ButlerDefs, null, true, false), result);
        Assert.Equal("awaitingOptions", control.State);
        Assert.True(control.OptionsStuck);
        Assert.Equal(0, host.StopCount);
        Assert.True(SkuaCommandHandler.FirstRunOptionsStuckAfter < TimeSpan.FromSeconds(15), "answered well before SkuaLink gives up");

        // The owner closes it on the PC.
        options.Window = () => SkuaOptionsWindow.None;
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("running", control.State);
        Assert.False(control.OptionsStuck);
    }

    [Fact]
    public async Task The_first_start_of_a_script_without_options_just_runs()
    {
        var (handler, host, options, control) = NewHandler();

        var result = await handler.HandleAsync(new StartCommand("r1", Plain));

        Assert.Equal(new ResultMessage("r1", true, null, null, [], null, false, false), result);
        Assert.Equal("running", control.State);
        Assert.Equal(0, host.StopCount);
        Assert.Equal(0, options.Finds);
    }

    // ---- later starts -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_later_start_answers_at_once_then_closes_the_dialog_and_runs()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        options.Window = options.ClosableDialog(appearsAfterSeconds: 1);
        var stops = host.StopCount;

        var result = await handler.HandleAsync(new StartCommand("r1", Butler));

        Assert.Equal(new ResultMessage("r1", true, null, null, ButlerDefs, null, true, false), result);
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(options.Closes);
        Assert.Equal("running", control.State);
        Assert.Equal(stops, host.StopCount);
    }

    [Fact]
    public async Task A_later_start_with_no_dialog_runs_after_5_seconds()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        var started = Now();

        await handler.HandleAsync(new StartCommand("r1", Butler));
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("running", control.State);
        Assert.InRange(Now() - started, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6));
        Assert.Empty(options.Closes);
    }

    [Fact]
    public async Task A_dialog_still_open_after_10_seconds_is_reported_stuck_and_left_to_the_owner()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        var started = Now();
        var stuckSeen = false;
        options.Window = () =>
        {
            stuckSeen |= control.OptionsStuck;
            return Now() - started < TimeSpan.FromSeconds(30) ? SkuaOptionsWindow.Open : SkuaOptionsWindow.None;
        };

        await handler.HandleAsync(new StartCommand("r1", Butler));
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(stuckSeen);
        Assert.NotEmpty(options.Closes);
        Assert.All(options.Closes, at => Assert.True(at - started < TimeSpan.FromSeconds(10), "no close attempt once stuck"));
        Assert.Equal("running", control.State);
        Assert.False(control.OptionsStuck);
    }

    [Fact]
    public async Task Window_code_that_fails_never_escapes_and_ends_stuck()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        var started = Now();
        var stuckSeen = false;
        options.Window = () =>
        {
            stuckSeen |= control.OptionsStuck;
            return Now() - started < TimeSpan.FromSeconds(20) ? throw new InvalidOperationException("no WPF") : SkuaOptionsWindow.None;
        };

        var result = await handler.HandleAsync(new StartCommand("r1", Butler));
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Ok);
        Assert.True(stuckSeen);
        Assert.Equal("running", control.State);
    }

    [Fact]
    public async Task A_window_that_cannot_be_read_is_never_taken_for_no_dialog()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        var started = Now();
        var states = new List<string>();
        options.Window = () =>
        {
            states.Add(control.State);
            return Now() - started < TimeSpan.FromSeconds(12) ? SkuaOptionsWindow.Unknown : SkuaOptionsWindow.Open;
        };

        await handler.HandleAsync(new StartCommand("r1", Butler));
        while (!control.OptionsStuck)
        {
            await Task.Delay(10);
        }

        Assert.Equal("awaitingOptions", control.State);
        await handler.HandleAsync(new StopCommand("r2"));
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("running", states);
    }

    [Fact]
    public async Task A_stop_while_the_dialog_is_open_still_closes_it_and_stays_stopping()
    {
        var (handler, host, options, control) = NewHandler();
        await Discover(handler, host, options, control);
        var gate = new ManualResetEventSlim();
        var calls = 0;
        options.Window = () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                gate.Wait(TimeSpan.FromSeconds(5));
            }

            return options.Closes.Count == 0 ? SkuaOptionsWindow.Open : SkuaOptionsWindow.None;
        };

        await handler.HandleAsync(new StartCommand("r1", Butler));
        await handler.HandleAsync(new StopCommand("r2"));
        gate.Set();
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(options.Closes);
        Assert.Equal("stopping", control.State);
    }

    [Fact]
    public async Task Options_read_from_another_script_than_the_one_started_are_not_kept()
    {
        var (handler, host, options, control) = NewHandler();
        host.OptionsFor = _ => new SkuaLoadedOptions(At("Farm/Gold.cs"), ButlerFile, ButlerOptions);
        host.HasOptions = true;

        var result = await handler.HandleAsync(new StartCommand("r1", Butler));
        var get = await handler.HandleAsync(new GetOptionsCommand("r2", Butler));

        Assert.Equal(new ResultMessage("r1", true, null, null, null, null, true, false), result);
        Assert.Null(get.OptionDefs);
        await handler.HandleAsync(new StopCommand("r3"));
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Options_that_cannot_be_read_fall_back_to_the_option_count_and_do_not_stop_the_script()
    {
        var (handler, host, options, control) = NewHandler();
        host.OptionsFor = _ => throw new InvalidOperationException("config");
        host.HasOptions = true;

        var result = await handler.HandleAsync(new StartCommand("r1", Butler));
        await handler.PendingOptions.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new ResultMessage("r1", true, null, null, null, null, true, false), result);
        Assert.Equal("running", control.State);
        Assert.Equal(0, host.StopCount);
    }

    [Fact]
    public async Task Without_an_options_host_options_commands_are_not_supported()
    {
        var host = new FakeHost();
        var catalog = new SkuaScriptCatalog(Root, _ => [At("Tools/Butler.cs")], _ => false);
        var handler = new SkuaCommandHandler(host, catalog, new SkuaControlState(Now), Now);

        var get = await handler.HandleAsync(new GetOptionsCommand("r1", Butler));

        Assert.Equal(new ResultMessage("r1", false, "not supported", null, null, null, false, false), get);
    }

    // ---- stop rulings -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_stop_before_skua_reports_the_start_still_stops_the_script_once_it_runs()
    {
        var (handler, host, _, control) = NewHandler();
        host.RunsOnStart = false;
        await handler.HandleAsync(new StartCommand("r1", Plain));

        var stop = await handler.HandleAsync(new StopCommand("r2"));
        Assert.True(stop.Ok);
        Assert.Equal(0, host.StopCount);

        host.Running = true;
        handler.Tick();
        await host.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.PendingStop.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Tick();
        handler.Tick();

        Assert.Equal(1, host.StopCount);
        Assert.Equal("stopping", control.State);
    }

    [Fact]
    public async Task Tick_does_not_stop_a_script_that_was_not_asked_to_stop()
    {
        var (handler, host, _, control) = NewHandler();
        await handler.HandleAsync(new StartCommand("r1", Plain));

        handler.Tick();
        handler.Tick();

        Assert.Equal(0, host.StopCount);
        Assert.Equal("running", control.State);
    }

    // ---- fakes ------------------------------------------------------------------------------------------------------

    private sealed class FakeHost : ISkuaScriptHost
    {
        private int _stopCount;
        private string _loaded = "";

        public volatile bool Running;
        public bool RunsOnStart { get; set; } = true;
        public bool HasOptions { get; set; }
        public Func<string, SkuaLoadedOptions?> OptionsFor { get; set; } = _ => null;
        public TaskCompletionSource StopCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset StopAt { get; private set; } = DateTimeOffset.MaxValue;
        public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.MinValue;
        public int StopCount => Volatile.Read(ref _stopCount);

        public bool ScriptRunning => Running;

        public bool ScriptExists(string fullPath) => true;

        public void SetLoadedScript(string fullPath) => _loaded = fullPath;

        public Task<Exception?> StartScript()
        {
            if (RunsOnStart)
            {
                Running = true;
            }

            return Task.FromResult<Exception?>(null);
        }

        public bool LoadedScriptHasOptions() => HasOptions;

        public SkuaLoadedOptions? LoadedOptions() => OptionsFor(_loaded);

        public Task StopScript()
        {
            StopAt = Clock();
            Interlocked.Increment(ref _stopCount);
            StopCalled.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOptions : ISkuaOptionsHost
    {
        private readonly FakeHost _host;
        private int _finds;

        public FakeOptions(FakeHost host) => _host = host;

        public Dictionary<string, IReadOnlyList<string>> Files { get; } = [];
        public List<string> Reads { get; } = [];
        public List<(string Path, IReadOnlyList<string> Lines)> Writes { get; } = [];
        public List<DateTimeOffset> Closes { get; } = [];
        public bool ReadThrows { get; set; }
        public bool WriteThrows { get; set; }
        public Func<SkuaOptionsWindow> Window { get; set; } = () => SkuaOptionsWindow.None;
        public Func<DateTimeOffset>? Clock { get; set; }
        public int Finds => Volatile.Read(ref _finds);

        public string OptionsRoot => SkuaCommandHandlerOptionsTests.OptionsRoot;

        public CultureInfo FileCulture { get; set; } = CultureInfo.InvariantCulture;

        public SkuaLoadedOptions? ReadLoadedOptions() => _host.LoadedOptions();

        public IReadOnlyList<string>? ReadOptionsFile(string path)
        {
            Reads.Add(path);
            return ReadThrows ? throw new IOException(@"C:\Users\Someone\AppData\Roaming\Skua\options\Butler.cfg is locked") : Files.GetValueOrDefault(path);
        }

        public void WriteOptionsFile(string path, IReadOnlyList<string> lines)
        {
            if (WriteThrows)
            {
                throw new UnauthorizedAccessException("denied");
            }

            Writes.Add((path, lines));
            Files[path] = lines;
        }

        public SkuaOptionsWindow FindOptionsWindow()
        {
            Interlocked.Increment(ref _finds);
            return Window();
        }

        public void CloseOptionsWindow()
        {
            lock (Closes)
            {
                Closes.Add(Clock!());
            }
        }

        /// <summary>A dialog that opens this long after the first look and closes on the first close.</summary>
        public Func<SkuaOptionsWindow> ClosableDialog(double appearsAfterSeconds = 0)
        {
            DateTimeOffset? first = null;
            return () =>
            {
                var now = Clock!();
                first ??= now;
                if (Closes.Count > 0)
                {
                    return SkuaOptionsWindow.None;
                }

                return now - first.Value >= TimeSpan.FromSeconds(appearsAfterSeconds) ? SkuaOptionsWindow.Open : SkuaOptionsWindow.None;
            };
        }
    }
}
