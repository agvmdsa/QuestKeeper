using AqwCompanion.App.Infrastructure.Browser;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeBrowserHost : IBrowserHost, INavigationFailureSource
{
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler? NavigationFailed;

    /// <summary>Number of handlers currently subscribed to <see cref="NavigationFailed"/>.</summary>
    public int FailureSubscribers => NavigationFailed?.GetInvocationList().Length ?? 0;

    public string? LastNavigated { get; private set; }

    public int CaptureCount { get; private set; }

    public int NavigateCount { get; private set; }

    /// <summary>Number of handlers currently subscribed to <see cref="NavigationCompleted"/>.</summary>
    public int Subscribers => NavigationCompleted?.GetInvocationList().Length ?? 0;

    /// <summary>What <see cref="CaptureAsync"/> returns; null means capturing is not supported.</summary>
    public Func<PageSnapshot>? OnCapture { get; set; }

    /// <summary>When set, takes precedence over <see cref="OnCapture"/> and lets a test control completion.</summary>
    public Func<CancellationToken, Task<PageSnapshot>>? OnCaptureAsync { get; set; }

    public void Navigate(string url)
    {
        NavigateCount++;
        LastNavigated = url;
    }

    public Task<PageSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        CaptureCount++;
        if (OnCaptureAsync is not null)
        {
            return OnCaptureAsync(ct);
        }

        return OnCapture is null ? throw new NotSupportedException() : Task.FromResult(OnCapture());
    }

    public void RaiseFailure() => NavigationFailed?.Invoke(this, EventArgs.Empty);

    public void Raise(string url) => NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(url));
}
