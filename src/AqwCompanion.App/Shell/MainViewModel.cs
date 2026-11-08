using AqwCompanion.App.Debug;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.GameData;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Settings;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Shell;

public sealed partial class MainViewModel(
    DiagnosticsViewModel diagnostics,
    HardFarmsViewModel hardFarms,
    GameDataViewModel gameData,
    SettingsViewModel settings,
    ISettingsDialog settingsDialog,
    DebugModeController debug)
{
    public string Title { get; } = AppInfo.ProductName;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
    public HardFarmsViewModel HardFarms { get; } = hardFarms;
    public GameDataViewModel GameData { get; } = gameData;
    public SettingsViewModel Settings { get; } = settings;
    public DebugModeController Debug { get; } = debug;

    /// <summary>Opens the Settings dialog over the window.</summary>
    [RelayCommand]
    private void ShowSettings() => settingsDialog.Show(Settings);

    /// <summary>Opens or closes the debug window (Ctrl+Shift+D).</summary>
    [RelayCommand]
    private void ToggleDebug() => Debug.Toggle();
}
