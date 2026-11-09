using AqwCompanion.App.Feed;
using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Feed;

public class LocalFeedControllerTests
{
    private static (SettingsViewModel Settings, FakeLocalFeedHost Host, LocalFeedController Controller) Build(bool saved = false, bool canStart = true)
    {
        var settings = new SettingsViewModel(new FakeAppSettingsStore(new AppSettings(LocalFeed: saved)), new ForgetCoordinator(() => true, () => []));
        var host = new FakeLocalFeedHost { CanStart = canStart };
        return (settings, host, new LocalFeedController(settings, host));
    }

    [Fact]
    public void Off_by_default_it_starts_nothing_and_says_off()
    {
        var (settings, host, controller) = Build();

        controller.Start();

        Assert.Equal(0, host.Starts);
        Assert.False(host.IsRunning);
        Assert.Equal("Off", settings.FeedStatusText);
    }

    [Fact]
    public void A_saved_on_setting_starts_the_feed_when_the_app_starts()
    {
        var (settings, host, controller) = Build(saved: true);

        controller.Start();

        Assert.True(host.IsRunning);
        Assert.Equal("Listening on 127.0.0.1:47821", settings.FeedStatusText);
    }

    [Fact]
    public void The_switch_starts_and_stops_the_feed()
    {
        var (settings, host, _) = Build();

        settings.LocalFeedEnabled = true;
        Assert.True(host.IsRunning);
        Assert.Equal("Listening on 127.0.0.1:47821", settings.FeedStatusText);

        settings.LocalFeedEnabled = false;
        Assert.False(host.IsRunning);
        Assert.Equal("Off", settings.FeedStatusText);
    }

    [Fact]
    public void A_port_that_cannot_be_used_is_said_in_words_and_nothing_runs()
    {
        var (settings, host, _) = Build(canStart: false);

        settings.LocalFeedEnabled = true;

        Assert.False(host.IsRunning);
        Assert.Equal("Could not listen on 127.0.0.1:47821 (the port may be in use)", settings.FeedStatusText);
    }

    [Fact]
    public void Starting_again_while_running_does_not_start_a_second_listener()
    {
        var (_, host, controller) = Build(saved: true);

        controller.Start();
        controller.Start();

        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public void The_status_starts_as_off_even_before_the_first_start()
    {
        var (settings, _, _) = Build(saved: true);

        Assert.Equal("Off", settings.FeedStatusText);
    }
}
