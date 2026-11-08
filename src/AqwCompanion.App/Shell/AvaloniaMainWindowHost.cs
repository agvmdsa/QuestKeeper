using Avalonia.Controls;
using Avalonia.Threading;

namespace AqwCompanion.App.Shell;

/// <summary>The real main window behind <see cref="IMainWindowHost"/>; does nothing until the window is attached. Safe to call from any thread.</summary>
public sealed class AvaloniaMainWindowHost : IMainWindowHost
{
    private Window? _window;

    /// <summary>Tells the host which window it controls.</summary>
    public void Attach(Window window) => _window = window;

    public void Show() => OnUiThread(() =>
    {
        if (_window is not { } window)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    });

    public void Hide() => OnUiThread(() => _window?.Hide());

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
