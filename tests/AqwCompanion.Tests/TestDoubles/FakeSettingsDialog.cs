using AqwCompanion.App.Settings;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeSettingsDialog : ISettingsDialog
{
    public List<SettingsViewModel> Shown { get; } = [];

    public void Show(SettingsViewModel settings) => Shown.Add(settings);
}
