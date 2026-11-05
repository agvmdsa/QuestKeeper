using AqwCompanion.Core.Ingestion;
using Avalonia.Controls;
using Serilog;

namespace AqwCompanion.App.Browser;

/// <summary>
/// Adapts an <see cref="NativeWebView"/> to <see cref="IBrowserHost"/>. The control is created by the sign-in
/// window each time it opens and attached then (and detached when it closes), so constructing this adapter
/// never touches the native browser.
/// The only script it runs is a read of the page HTML.
/// </summary>
public sealed class WebViewBrowserHost : IBrowserHost, INavigationFailureSource
{
    internal const string ReadHtmlScript = "document.documentElement.outerHTML";

    private readonly IUiThread _ui;
    private NativeWebView? _view;
    private Func<string, Task<string?>>? _runScript;
    private Action<Uri>? _navigate;
    private Action? _unsubscribe;

    // Written only on the UI thread (callbacks from the native adapter are marshalled first).
    private bool _adapterReady;
    private string? _currentUrl;

    // Bumped by Detach: callbacks queued by a control that has since been detached are dropped.
    private int _generation;

    public WebViewBrowserHost()
        : this(new AvaloniaUiThread())
    {
    }

    internal WebViewBrowserHost(IUiThread ui) => _ui = ui;

    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;

    /// <summary>Raised when a navigation did not complete successfully; carries no address.</summary>
    public event EventHandler? NavigationFailed;

    public bool IsAttached => _view is not null;

    internal bool IsAdapterReady => _adapterReady;

    internal string? CurrentUrl => _currentUrl;

    /// <summary>Address requested while no control or adapter existed; it is opened as soon as the adapter is created.</summary>
    public string? PendingUrl { get; private set; }

    /// <summary>
    /// Starts using <paramref name="view"/>. Attaching while another control is attached detaches that one first,
    /// so the host can serve a new control each time a window that owns one is opened.
    /// </summary>
    public void Attach(NativeWebView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (ReferenceEquals(_view, view))
        {
            return;
        }

        if (_view is not null)
        {
            Detach();
        }

        _view = view;
        UseScriptRunner(view.InvokeScript);
        UseNavigator(view.Navigate);

        void OnAdapterCreated(object? sender, EventArgs e) => HandleAdapterCreated();
        void OnAdapterDestroyed(object? sender, EventArgs e) => HandleAdapterDestroyed();
        void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e) =>
            HandleNavigationCompleted(e.IsSuccess, e.Request);

        view.AdapterCreated += OnAdapterCreated;
        view.AdapterDestroyed += OnAdapterDestroyed;
        view.NavigationCompleted += OnNavigationCompleted;
        _unsubscribe = () =>
        {
            view.AdapterCreated -= OnAdapterCreated;
            view.AdapterDestroyed -= OnAdapterDestroyed;
            view.NavigationCompleted -= OnNavigationCompleted;
        };
    }

    /// <summary>
    /// Lets go of the attached control: its events are unsubscribed and all page state is cleared, so a later
    /// <see cref="Attach"/> starts from scratch. Does nothing when no control is attached.
    /// </summary>
    public void Detach()
    {
        _unsubscribe?.Invoke();
        _unsubscribe = null;
        _view = null;
        _runScript = null;
        _navigate = null;
        _adapterReady = false;
        _currentUrl = null;
        PendingUrl = null;
        _generation++;
    }

    /// <summary>How a script reaches the page; the control's own <c>InvokeScript</c> in production.</summary>
    internal void UseScriptRunner(Func<string, Task<string?>> runScript) => _runScript = runScript;

    /// <summary>How an address is opened; the control's own <c>Navigate</c> in production.</summary>
    internal void UseNavigator(Action<Uri> navigate) => _navigate = navigate;

    public void Navigate(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);

        // Without a control, or before its native adapter exists, the address waits and is applied once
        // the adapter is created.
        if (_navigate is not { } navigate || !_adapterReady)
        {
            PendingUrl = uri.AbsoluteUri;
            return;
        }

        PendingUrl = null;
        navigate(uri);
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

    internal void HandleAdapterCreated() =>
        RunOnUiThread(() =>
        {
            _adapterReady = true;
            if (PendingUrl is { } pending && _navigate is { } navigate)
            {
                PendingUrl = null;
                navigate(new Uri(pending, UriKind.Absolute));
            }
        });

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
                NavigationFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            _currentUrl = url;
            NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(url));
        });

    private void RunOnUiThread(Action action)
    {
        var generation = Volatile.Read(ref _generation);
        void Guarded()
        {
            if (generation == _generation)
            {
                action();
            }
        }

        if (_ui.CheckAccess())
        {
            Guarded();
        }
        else
        {
            _ui.Post(Guarded);
        }
    }
}
