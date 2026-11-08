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
    ISettingsDialog settingsDialog)
{
    public string Title { get; } = AppInfo.ProductName;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
    public HardFarmsViewModel HardFarms { get; } = hardFarms;
    public GameDataViewModel GameData { get; } = gameData;
    public SettingsViewModel Settings { get; } = settings;

    /// <summary>Opens the Settings dialog over the window.</summary>
    [RelayCommand]
    private void ShowSettings() => settingsDialog.Show(Settings);
}
