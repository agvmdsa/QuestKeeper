using AqwCompanion.App.Settings;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class FakeAppSettingsStore(AppSettings? initial = null) : IAppSettingsStore
{
    public AppSettings Current { get; private set; } = initial ?? new AppSettings();

    public int Saves { get; private set; }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        Current = settings;
        Saves++;
    }
}
