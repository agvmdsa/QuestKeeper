namespace AqwCompanion.App.Settings;

/// <summary>The player's preferences. They are not data about the player, so Forgetting keeps them.</summary>
public sealed record AppSettings(bool DebugMode = false);

/// <summary>Keeps <see cref="AppSettings"/> between runs. Never throws.</summary>
public interface IAppSettingsStore
{
    /// <summary>The saved settings, or the defaults when there is no usable file.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
