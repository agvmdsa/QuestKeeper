using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace AqwCompanion.App.GameData;

/// <summary>Shuts the desktop application down; does nothing when there is no desktop lifetime.</summary>
public sealed class AvaloniaApplicationExit : IApplicationExit
{
    public void Exit() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
