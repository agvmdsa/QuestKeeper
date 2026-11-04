using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.App;

/// <summary>v0.1 stand-in until the WebView adapter exists.</summary>
public sealed class NullBrowserHost : IBrowserHost
{
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted
    {
        add { }
        remove { }
    }

    public void Navigate(string url)
    {
    }

    public Task<PageSnapshot> CaptureAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("No browser is attached yet.");
}
