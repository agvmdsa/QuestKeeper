using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeBrowserHost : IBrowserHost
{
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;

    public string? LastNavigated { get; private set; }

    public void Navigate(string url) => LastNavigated = url;

    public Task<PageSnapshot> CaptureAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public void Raise(string url) => NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(url));
}
