using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;

namespace AqwCompanion.App.Debug;

/// <summary>What the debug window shows: the capture controls and numbers, the Hard Farms entries without a match, and the diagnostics export.</summary>
public sealed class DebugViewModel(GameDataViewModel gameData, DiagnosticsViewModel diagnostics, HardFarmsViewModel hardFarms)
{
    public GameDataViewModel GameData { get; } = gameData;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
    public HardFarmsViewModel HardFarms { get; } = hardFarms;
}
