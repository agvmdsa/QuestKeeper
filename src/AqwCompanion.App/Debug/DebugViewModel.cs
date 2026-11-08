using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;

namespace AqwCompanion.App.Debug;

/// <summary>What the debug window shows: the capture controls and numbers, and the diagnostics export.</summary>
public sealed class DebugViewModel(GameDataViewModel gameData, DiagnosticsViewModel diagnostics)
{
    public GameDataViewModel GameData { get; } = gameData;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
}
