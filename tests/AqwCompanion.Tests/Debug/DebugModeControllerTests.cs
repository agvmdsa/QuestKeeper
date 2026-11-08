using AqwCompanion.App.Debug;
using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Debug;

public class DebugModeControllerTests
{
    private readonly FakeAppSettingsStore _store;
    private readonly SettingsViewModel _settings;
    private readonly FakeDebugWindowHost _host = new();
    private readonly DebugModeController _controller;

    public DebugModeControllerTests()
    {
        _store = new FakeAppSettingsStore();
        _settings = new SettingsViewModel(_store, new ForgetCoordinator(() => true, () => []));
        _controller = new DebugModeController(_settings, _host);
    }

    [Fact]
    public void Starting_with_debug_mode_off_opens_nothing()
    {
        _controller.Start();

        Assert.False(_host.IsOpen);
    }

    [Fact]
    public void Starting_with_debug_mode_saved_on_opens_the_window()
    {
        var settings = new SettingsViewModel(new FakeAppSettingsStore(new AppSettings(true)), new ForgetCoordinator(() => true, () => []));
        var host = new FakeDebugWindowHost();

        new DebugModeController(settings, host).Start();

        Assert.True(host.IsOpen);
    }

    [Fact]
    public void Turning_the_switch_on_and_off_opens_and_closes_the_window()
    {
        _settings.DebugMode = true;
        Assert.True(_host.IsOpen);

        _settings.DebugMode = false;
        Assert.False(_host.IsOpen);
    }

    [Fact]
    public void Toggle_flips_the_mode_and_the_window_follows()
    {
        _controller.Toggle();
        Assert.True(_settings.DebugMode);
        Assert.True(_host.IsOpen);

        _controller.Toggle();
        Assert.False(_settings.DebugMode);
        Assert.False(_host.IsOpen);
    }

    [Fact]
    public void Closing_the_window_with_its_own_button_turns_the_mode_off_and_saves_it()
    {
        _settings.DebugMode = true;

        _host.CloseByUser();

        Assert.False(_settings.DebugMode);
        Assert.False(_store.Current.DebugMode);
    }

    [Fact]
    public void Turning_the_mode_on_twice_opens_the_window_once()
    {
        _settings.DebugMode = true;
        _settings.DebugMode = true;

        Assert.Equal(1, _host.Shows);
    }
}
