using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AqwCompanion.App.Shell;

/// <summary>
/// Builds the notification-area icon and its menu (Open, Start or Stop capture, Debug mode, Exit) over a <see cref="TrayController"/>.
/// Deliberately thin and not covered by tests: it needs a real desktop platform. Create and dispose it on the UI thread.
/// </summary>
public sealed class TrayIconSetup : IDisposable
{
    private readonly TrayController _controller;
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _capture;
    private bool _disposed;

    public TrayIconSetup(TrayController controller)
    {
        _controller = controller;
        _capture = new NativeMenuItem(controller.CaptureMenuText);
        _capture.Click += (_, _) => controller.ToggleCapture();
        var open = new NativeMenuItem("Open");
        open.Click += (_, _) => controller.Open();
        var debug = new NativeMenuItem("Debug mode");
        debug.Click += (_, _) => controller.ToggleDebug();
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => _ = controller.ExitAsync();

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(_capture);
        menu.Items.Add(debug);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://" + AppInfo.ExeName + "/Assets/app.png"))),
            ToolTipText = controller.ToolTip,
            Menu = menu,
            IsVisible = true,
        };
        _icon.Clicked += (_, _) => controller.Open();
        controller.Changed += OnChanged;
    }

    /// <summary>The icon, for registering it with the application.</summary>
    public TrayIcon Icon => _icon;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Changed -= OnChanged;
        _icon.IsVisible = false;
        _icon.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _icon.ToolTipText = _controller.ToolTip;
        _capture.Header = _controller.CaptureMenuText;
    }
}
