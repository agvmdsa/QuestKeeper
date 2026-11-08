using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace AqwCompanion.App.Settings;

/// <summary>Shows <see cref="SettingsWindow"/> as a dialog of the main window; a second request brings the open one forward.</summary>
public sealed class AvaloniaSettingsDialog : ISettingsDialog
{
    private SettingsWindow? _open;

    public void Show(SettingsViewModel settings)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new SettingsWindow { DataContext = settings };
        void Close(object? sender, EventArgs e) => window.Close();
        settings.CloseRequested += Close;
        window.Closed += (_, _) =>
        {
            settings.CloseRequested -= Close;
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
}
