using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Reputation;
using AqwCompanion.App.Wiki;

namespace AqwCompanion.App.Shell;

public sealed class MainViewModel
{
    public MainViewModel(WikiViewModel wiki, DiagnosticsViewModel diagnostics, HardFarmsViewModel hardFarms,
        ReputationViewModel reputation, GameDataViewModel gameData)
    {
        Wiki = wiki;
        Diagnostics = diagnostics;
        HardFarms = hardFarms;
        Reputation = reputation;
        GameData = gameData;
    }

    public string Title { get; } = AppInfo.ProductName;
    public WikiViewModel Wiki { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public HardFarmsViewModel HardFarms { get; }
    public ReputationViewModel Reputation { get; }
    public GameDataViewModel GameData { get; }
}
