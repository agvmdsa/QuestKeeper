using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;

namespace AqwCompanion.App.Debug;

/// <summary>Opens <see cref="DebugWindow"/> as an ordinary window next to the main one; showing it twice brings the open one forward.</summary>
public sealed class AvaloniaDebugWindowHost(GameDataViewModel gameData, DiagnosticsViewModel diagnostics, HardFarmsViewModel hardFarms) : IDebugWindowHost
{
    private DebugWindow? _window;
    private bool _closingByCode;

    public event EventHandler? Closed;

    public void Show()
    {
        if (_window is { } open)
        {
            open.Activate();
            return;
        }

        var window = new DebugWindow { DataContext = new DebugViewModel(gameData, diagnostics, hardFarms) };
        window.Closed += (_, _) =>
        {
            _window = null;
            if (!_closingByCode)
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        };
        _window = window;
        window.Show();
    }

    public void Close()
    {
        if (_window is not { } window)
        {
            return;
        }

        _closingByCode = true;
        try
        {
            window.Close();
        }
        finally
        {
            _closingByCode = false;
        }
    }
}
