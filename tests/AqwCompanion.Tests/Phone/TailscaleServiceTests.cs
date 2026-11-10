using AqwCompanion.App.Phone;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public sealed class TailscaleServiceTests
{
    private static readonly string ProgramFiles = Path.Combine(Path.GetTempPath(), "Program Files");
    private static readonly string ExpectedExe = Path.Combine(ProgramFiles, "Tailscale", "tailscale.exe");

    private const string ReadyJson =
        """{"BackendState":"Running","Self":{"DNSName":"my-pc.tail1234.ts.net."},"CertDomains":["my-pc.tail1234.ts.net"]}""";

    private readonly FakeTailscaleProcess _process = new();
    private readonly List<string> _existsChecked = [];
    private bool _exists = true;

    private TailscaleService Create() => new(_process, ProgramFiles, p => { _existsChecked.Add(p); return _exists; });

    [Fact]
    public async Task Missing_file_is_not_installed_and_nothing_runs()
    {
        _exists = false;

        var state = await Create().GetStateAsync(CancellationToken.None);

        Assert.Equal(TailscaleStateKind.NotInstalled, state.Kind);
        Assert.Equal(TailscaleService.NotInstalledText, state.Text);
        Assert.Empty(_process.Calls);
        Assert.Equal([ExpectedExe], _existsChecked);
    }

    [Fact]
    public async Task Serve_and_stop_do_nothing_when_not_installed()
    {
        _exists = false;
        var service = Create();

        Assert.False(await service.ServeAsync(47822, CancellationToken.None));
        Assert.False(await service.StopServeAsync(CancellationToken.None));
        Assert.Empty(_process.Calls);
    }

    [Fact]
    public async Task A_relative_exe_path_is_not_installed_and_nothing_runs()
    {
        var service = new TailscaleService(_process, "", _ => true);

        Assert.Equal(TailscaleStateKind.NotInstalled, (await service.GetStateAsync(CancellationToken.None)).Kind);
        Assert.False(await service.ServeAsync(47822, CancellationToken.None));
        Assert.False(await service.StopServeAsync(CancellationToken.None));
        Assert.Empty(_process.Calls);
    }

    [Fact]
    public async Task Status_runs_exactly_the_fixed_command()
    {
        _process.Result = new(0, ReadyJson, false, false);

        var state = await Create().GetStateAsync(CancellationToken.None);

        var call = Assert.Single(_process.Calls);
        Assert.Equal(ExpectedExe, call.Exe);
        Assert.Equal(["status", "--json"], call.Args);
        Assert.Equal(TimeSpan.FromSeconds(10), call.Timeout);
        Assert.Equal(256 * 1024, call.MaxBytes);
        Assert.Equal(TailscaleStateKind.Ready, state.Kind);
        Assert.Equal("my-pc.tail1234.ts.net", state.DnsName);
    }

    [Fact]
    public async Task Status_with_a_stopped_backend_is_not_signed_in_even_with_a_non_zero_exit()
    {
        _process.Result = new(1, """{"BackendState":"Stopped"}""", false, false);

        Assert.Equal(TailscaleStateKind.NotSignedIn, (await Create().GetStateAsync(CancellationToken.None)).Kind);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Status_timeout_or_oversized_output_is_an_error(bool timedOut, bool truncated)
    {
        _process.Result = new(0, ReadyJson, timedOut, truncated);

        var state = await Create().GetStateAsync(CancellationToken.None);

        Assert.Equal(TailscaleStateKind.Error, state.Kind);
        Assert.Null(state.DnsName);
    }

    [Fact]
    public async Task Status_process_exception_is_an_error()
    {
        _process.Throws = new InvalidOperationException("boom my-pc.tail1234.ts.net");

        var state = await Create().GetStateAsync(CancellationToken.None);

        Assert.Equal(TailscaleStateKind.Error, state.Kind);
        Assert.DoesNotContain("boom", state.Text);
    }

    [Fact]
    public async Task Serve_runs_exactly_the_fixed_command_and_reports_the_exit_code()
    {
        var service = Create();

        Assert.True(await service.ServeAsync(47822, CancellationToken.None));
        _process.Result = new(1, "", false, false);
        Assert.False(await service.ServeAsync(47822, CancellationToken.None));

        Assert.All(_process.Calls, c => Assert.Equal(ExpectedExe, c.Exe));
        Assert.All(_process.Calls, c => Assert.Equal(["serve", "--bg", "http://127.0.0.1:47822"], c.Args));
        Assert.All(_process.Calls, c => Assert.Equal(TimeSpan.FromSeconds(10), c.Timeout));
    }

    [Fact]
    public async Task Serve_port_is_formatted_invariantly()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            await Create().ServeAsync(47822, CancellationToken.None);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }

        Assert.Equal(["serve", "--bg", "http://127.0.0.1:47822"], _process.Calls[0].Args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task Serve_rejects_an_impossible_port_without_running(int port)
    {
        Assert.False(await Create().ServeAsync(port, CancellationToken.None));
        Assert.Empty(_process.Calls);
    }

    [Fact]
    public async Task Stop_runs_exactly_the_fixed_command()
    {
        Assert.True(await Create().StopServeAsync(CancellationToken.None));

        var call = Assert.Single(_process.Calls);
        Assert.Equal(ExpectedExe, call.Exe);
        Assert.Equal(["serve", "reset"], call.Args);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 2)]
    public async Task Serve_and_stop_fail_on_timeout_oversize_or_exit_code(bool timedOut, bool truncated, int exit)
    {
        _process.Result = new(exit, "", timedOut, truncated);
        var service = Create();

        Assert.False(await service.ServeAsync(47822, CancellationToken.None));
        Assert.False(await service.StopServeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Serve_and_stop_swallow_exceptions()
    {
        _process.Throws = new IOException("x");
        var service = Create();

        Assert.False(await service.ServeAsync(47822, CancellationToken.None));
        Assert.False(await service.StopServeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_call_is_an_error_state_not_a_throw()
    {
        _process.Throws = new OperationCanceledException();

        var state = await Create().GetStateAsync(CancellationToken.None);

        Assert.Equal(TailscaleStateKind.Error, state.Kind);
    }
}
