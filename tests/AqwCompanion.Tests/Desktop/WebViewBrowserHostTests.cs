using AqwCompanion.App.Browser;
using AqwCompanion.Core.Aqw;
using AqwCompanion.Core.Ingestion;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Headless.XUnit;

namespace AqwCompanion.Tests.Desktop;

/// <summary>Covers the behaviour that needs no native control; the attached path needs a real display.</summary>
public class WebViewBrowserHostTests
{
    private readonly FakeUiThread _ui = new();

    [Fact]
    public void Navigate_before_attach_is_remembered_and_does_not_throw()
    {
        var host = new WebViewBrowserHost();

        var error = Record.Exception(() => host.Navigate(AqwUrls.Home));

        Assert.Null(error);
        Assert.False(host.IsAttached);
        Assert.Equal(AqwUrls.Home, host.PendingUrl);
    }

    [Fact]
    public async Task Capture_before_attach_throws_invalid_operation()
    {
        var host = new WebViewBrowserHost();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.CaptureAsync());
    }

    private async Task<(WebViewBrowserHost Host, List<string> Scripts)> ReadyHostAsync(Func<Task<string?>>? script = null)
    {
        var scripts = new List<string>();
        var host = new WebViewBrowserHost(_ui);
        host.UseScriptRunner(s =>
        {
            scripts.Add(s);
            return script is null ? Task.FromResult<string?>("\"<html></html>\"") : script();
        });
        await _ui.RunOnUiThread(host.HandleAdapterCreated);
        return (host, scripts);
    }

    [Fact]
    public async Task Capture_while_the_adapter_is_not_ready_throws_and_never_runs_a_script()
    {
        var scripts = new List<string>();
        var host = new WebViewBrowserHost(_ui);
        host.UseScriptRunner(s =>
        {
            scripts.Add(s);
            return Task.FromResult<string?>(null);
        });
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CaptureAsync());

        Assert.Equal("The browser is not ready.", error.Message);
        Assert.Empty(scripts);
    }

    [Fact]
    public async Task Capture_reads_the_html_of_the_current_page_with_the_single_read_only_script()
    {
        var (host, scripts) = await ReadyHostAsync();
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));

        var snapshot = await host.CaptureAsync();

        Assert.Equal(AqwUrls.Home, snapshot.Url);
        Assert.Equal("<html></html>", snapshot.Payload);
        Assert.Equal(["document.documentElement.outerHTML"], scripts);
    }

    [Fact]
    public async Task Adapter_destroyed_clears_the_page_and_readiness()
    {
        var (host, scripts) = await ReadyHostAsync();
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));
        Assert.True(host.IsAdapterReady);
        Assert.NotNull(host.CurrentUrl);

        await _ui.RunOnUiThread(() => host.HandleAdapterDestroyed());

        Assert.False(host.IsAdapterReady);
        Assert.Null(host.CurrentUrl);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.CaptureAsync());
        Assert.Empty(scripts);
    }

    [Fact]
    public async Task Adapter_recreated_is_ready_again_but_needs_a_new_page_and_does_not_navigate_by_itself()
    {
        var (host, scripts) = await ReadyHostAsync();
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));
        await _ui.RunOnUiThread(() => host.HandleAdapterDestroyed());

        await _ui.RunOnUiThread(() => host.HandleAdapterCreated());

        Assert.True(host.IsAdapterReady);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CaptureAsync());
        Assert.Contains("No page", error.Message, StringComparison.Ordinal);
        Assert.Empty(scripts);
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));
        Assert.Equal(AqwUrls.Home, (await host.CaptureAsync()).Url);
    }

    [Fact]
    public async Task A_failed_navigation_raises_nothing_and_keeps_the_previous_page()
    {
        var (host, _) = await ReadyHostAsync();
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));
        var raised = 0;
        host.NavigationCompleted += (_, _) => raised++;

        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(false, new Uri("https://account.aq.com/Login")));

        Assert.Equal(0, raised);
        Assert.Equal(AqwUrls.Home, host.CurrentUrl);
    }

    [Fact]
    public async Task Navigation_completed_from_a_background_thread_is_raised_on_the_ui_thread()
    {
        var (host, _) = await ReadyHostAsync();
        var seen = new List<(string Url, int Thread)>();
        host.NavigationCompleted += (_, e) => seen.Add((e.Url, Environment.CurrentManagedThreadId));
        var callerThread = await Task.Run(() =>
        {
            host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home));
            return Environment.CurrentManagedThreadId;
        });
        await _ui.Flush();

        var (url, thread) = Assert.Single(seen);
        Assert.Equal(AqwUrls.Home, url);
        Assert.Equal(_ui.ThreadId, thread);
        Assert.NotEqual(callerThread, thread);
        Assert.Equal(AqwUrls.Home, host.CurrentUrl);
    }

    [Fact]
    public async Task Navigation_completed_on_the_ui_thread_is_raised_directly()
    {
        var (host, _) = await ReadyHostAsync();
        var raised = 0;
        host.NavigationCompleted += (_, _) => raised++;

        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));

        Assert.Equal(1, raised);
        Assert.Equal(0, _ui.PostedCount);
    }

    [Fact]
    public async Task Adapter_callbacks_from_a_background_thread_are_applied_on_the_ui_thread()
    {
        var host = new WebViewBrowserHost(_ui);

        await Task.Run(host.HandleAdapterCreated);
        await _ui.Flush();

        Assert.Equal(1, _ui.PostedCount);
        Assert.True(host.IsAdapterReady);
    }

    [Fact]
    public async Task Capture_from_a_background_thread_runs_the_script_on_the_ui_thread()
    {
        var scriptThread = -1;
        var (host, _) = await ReadyHostAsync(() =>
        {
            scriptThread = Environment.CurrentManagedThreadId;
            return Task.FromResult<string?>("\"<html></html>\"");
        });
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));
        await _ui.Flush();

        var snapshot = await Task.Run(() => host.CaptureAsync()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(_ui.ThreadId, scriptThread);
        Assert.Equal(AqwUrls.Home, snapshot.Url);
    }

    [Fact]
    public async Task Capture_on_the_ui_thread_does_not_deadlock()
    {
        var (host, _) = await ReadyHostAsync();
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));
        await _ui.Flush();

        var snapshot = await _ui.InvokeAsync(() => host.CaptureAsync()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AqwUrls.Home, snapshot.Url);
    }

    [Fact]
    public async Task Capture_honours_cancellation_while_the_script_is_pending()
    {
        var (host, _) = await ReadyHostAsync(() => new TaskCompletionSource<string?>().Task);
        await _ui.RunOnUiThread(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.CaptureAsync(cts.Token));
    }

    [AvaloniaFact]
    public async Task The_real_dispatcher_delivers_a_background_navigation_on_the_ui_thread()
    {
        var host = new WebViewBrowserHost();
        var uiThread = Environment.CurrentManagedThreadId;
        var delivered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.NavigationCompleted += (_, _) => delivered.TrySetResult(Environment.CurrentManagedThreadId);

        _ = Task.Run(() => host.HandleNavigationCompleted(true, new Uri(AqwUrls.Home)));

        Assert.Equal(uiThread, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
