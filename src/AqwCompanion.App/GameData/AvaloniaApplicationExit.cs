using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace AqwCompanion.App.GameData;

/// <summary>Shuts the desktop application down on the UI thread; does nothing when there is no desktop lifetime.</summary>
public sealed class AvaloniaApplicationExit : IApplicationExit
{
    public void Exit() => Dispatcher.UIThread.Post(() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
}
