using AqwCompanion.App.Phone;
using AqwCompanion.Phone.Devices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace AqwCompanion.App.Settings;

/// <summary>Shows <see cref="SettingsWindow"/> as a dialog of the main window; a second request brings the open one forward.</summary>
public sealed class AvaloniaSettingsDialog : ISettingsDialog
{
    private readonly IPairingLinkSource? _links;
    private readonly PhoneRegistry? _registry;
    private SettingsWindow? _open;
    private PairingWindow? _pairing;

    public AvaloniaSettingsDialog(IPairingLinkSource? links = null, PhoneRegistry? registry = null)
    {
        _links = links;
        _registry = registry;
    }

    public void Show(SettingsViewModel settings)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }

        settings.Refresh();
        var window = new SettingsWindow { DataContext = settings };
        void Close(object? sender, EventArgs e) => window.Close();
        void Pair(object? sender, EventArgs e) => ShowPairing(window);
        settings.CloseRequested += Close;
        settings.PairRequested += Pair;
        window.Closed += (_, _) =>
        {
            settings.CloseRequested -= Close;
            settings.PairRequested -= Pair;
            _open = null;
        };
        _open = window;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    // A new window and a fresh token each time; a second request brings the open one forward.
    private void ShowPairing(SettingsWindow owner)
    {
        if (_links is null || _registry is null)
        {
            return;
        }

        if (_pairing is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new PairingWindow();
        window.DataContext = new PairingViewModel(
            _links, _registry, () => DateTimeOffset.UtcNow, OnUiThread,
            text => window.Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : Task.CompletedTask);
        window.Closed += (_, _) => _pairing = null;
        _pairing = window;
        window.ShowDialog(owner);
    }

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
