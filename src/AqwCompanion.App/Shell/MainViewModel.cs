using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;

namespace AqwCompanion.App.Shell;

public sealed class MainViewModel
{
    public MainViewModel(DiagnosticsViewModel diagnostics, HardFarmsViewModel hardFarms, GameDataViewModel gameData)
    {
        Diagnostics = diagnostics;
        HardFarms = hardFarms;
        GameData = gameData;
    }

    public string Title { get; } = AppInfo.ProductName;
    public DiagnosticsViewModel Diagnostics { get; }
    public HardFarmsViewModel HardFarms { get; }
    public GameDataViewModel GameData { get; }
}
