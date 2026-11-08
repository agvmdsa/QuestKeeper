namespace AqwCompanion.App.Settings;

/// <summary>Opens the Settings dialog over the main window.</summary>
public interface ISettingsDialog
{
    void Show(SettingsViewModel settings);
}
