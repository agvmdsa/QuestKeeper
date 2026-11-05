using Avalonia.Threading;

namespace AqwCompanion.App.Browser;

/// <summary>Thread-marshalling seam so the browser host can be tested without a real dispatcher.</summary>
internal interface IUiThread
{
    bool CheckAccess();

    void Post(Action action);

    Task<T> InvokeAsync<T>(Func<Task<T>> work);
}

internal sealed class AvaloniaUiThread : IUiThread
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    // Already on the UI thread: run inline, never queue behind ourselves.
    public Task<T> InvokeAsync<T>(Func<Task<T>> work) =>
        CheckAccess() ? work() : Dispatcher.UIThread.InvokeAsync(work);
}
