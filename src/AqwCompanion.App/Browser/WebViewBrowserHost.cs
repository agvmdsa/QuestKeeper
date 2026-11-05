using AqwCompanion.Core.Ingestion;
using Avalonia.Controls;
using Serilog;

namespace AqwCompanion.App.Browser;

/// <summary>
/// Adapts an <see cref="NativeWebView"/> to <see cref="IBrowserHost"/>. The control is created by the view
/// on demand and attached later, so constructing this adapter never touches the native browser.
/// The only script it runs is a read of the page HTML.
/// </summary>
public sealed class WebViewBrowserHost : IBrowserHost
{
    internal const string ReadHtmlScript = "document.documentElement.outerHTML";

    private readonly IUiThread _ui;
    private NativeWebView? _view;
    private Func<string, Task<string?>>? _runScript;

    // Written only on the UI thread (callbacks from the native adapter are marshalled first).
    private bool _adapterReady;
    private string? _currentUrl;

    public WebViewBrowserHost()
        : this(new AvaloniaUiThread())
    {
    }

    internal WebViewBrowserHost(IUiThread ui) => _ui = ui;

    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;

    public bool IsAttached => _view is not null;

    internal bool IsAdapterReady => _adapterReady;

    internal string? CurrentUrl => _currentUrl;

    /// <summary>Address requested before a control was attached; it is opened on attach.</summary>
    public string? PendingUrl { get; private set; }

    public void Attach(NativeWebView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_view is not null)
        {
            throw new InvalidOperationException("A browser control is already attached.");
        }

        _view = view;
        UseScriptRunner(view.InvokeScript);
        _view.AdapterCreated += (_, _) => HandleAdapterCreated();
        _view.AdapterDestroyed += (_, _) => HandleAdapterDestroyed();
        _view.NavigationCompleted += (_, e) => HandleNavigationCompleted(e.IsSuccess, e.Request);
        if (PendingUrl is { } pending)
        {
            PendingUrl = null;
            _view.Source = new Uri(pending, UriKind.Absolute);
        }
    }

    /// <summary>How a script reaches the page; the control's own <c>InvokeScript</c> in production.</summary>
    internal void UseScriptRunner(Func<string, Task<string?>> runScript) => _runScript = runScript;

    public void Navigate(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        if (_view is null)
        {
            PendingUrl = uri.AbsoluteUri;
            return;
        }

        // Before the native adapter exists, Source is the value it opens with once created.
        if (_adapterReady)
        {
            _view.Navigate(uri);
        }
        else
        {
            _view.Source = uri;
        }
    }

    public async Task<PageSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_runScript is not { } runScript)
        {
            throw new InvalidOperationException("No page has been loaded in the browser yet.");
        }

        // State is checked and the script is started on the UI thread, so a control that was destroyed
        // in the meantime is never scripted.
        var (url, raw) = await _ui.InvokeAsync(async () =>
        {
            if (!_adapterReady)
            {
                throw new InvalidOperationException("The browser is not ready.");
            }

            if (_currentUrl is not { } current)
            {
                throw new InvalidOperationException("No page has been loaded in the browser yet.");
            }

            return (current, await runScript(ReadHtmlScript));
        }).WaitAsync(ct);
        return PageSnapshot.Create(url, ScriptResultDecoder.DecodeString(raw), AppInfo.Version, DateTimeOffset.UtcNow);
    }

    internal void HandleAdapterCreated() => RunOnUiThread(() => _adapterReady = true);

    // The native control is destroyed when detached (for example when a tab swaps its content): the page
    // is gone with it and nothing may be scripted until the adapter exists again.
    internal void HandleAdapterDestroyed() =>
        RunOnUiThread(() =>
        {
            _adapterReady = false;
            _currentUrl = null;
        });

    internal void HandleNavigationCompleted(bool isSuccess, Uri? requested) =>
        RunOnUiThread(() =>
        {
            var url = requested?.AbsoluteUri ?? _view?.Source?.AbsoluteUri;
            if (!isSuccess || url is null)
            {
                Log.Warning("Browser navigation did not complete successfully");
                return;
            }

            _currentUrl = url;
            NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(url));
        });

    private void RunOnUiThread(Action action)
    {
        if (_ui.CheckAccess())
        {
            action();
        }
        else
        {
            _ui.Post(action);
        }
    }
}
