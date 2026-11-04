namespace AqwCompanion.Core.Ingestion;

public sealed class NavigationCompletedEventArgs(string url) : EventArgs
{
    public string Url { get; } = url;
}

public interface IBrowserHost
{
    event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    void Navigate(string url);
    Task<PageSnapshot> CaptureAsync(CancellationToken ct = default);
}
