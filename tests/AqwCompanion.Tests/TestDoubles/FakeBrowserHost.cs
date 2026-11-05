using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeBrowserHost : IBrowserHost
{
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;

    public string? LastNavigated { get; private set; }

    public int CaptureCount { get; private set; }

    /// <summary>What <see cref="CaptureAsync"/> returns; null means capturing is not supported.</summary>
    public Func<PageSnapshot>? OnCapture { get; set; }

    /// <summary>When set, takes precedence over <see cref="OnCapture"/> and lets a test control completion.</summary>
    public Func<CancellationToken, Task<PageSnapshot>>? OnCaptureAsync { get; set; }

    public void Navigate(string url) => LastNavigated = url;

    public Task<PageSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        CaptureCount++;
        if (OnCaptureAsync is not null)
        {
            return OnCaptureAsync(ct);
        }

        return OnCapture is null ? throw new NotSupportedException() : Task.FromResult(OnCapture());
    }

    public void Raise(string url) => NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(url));
}
