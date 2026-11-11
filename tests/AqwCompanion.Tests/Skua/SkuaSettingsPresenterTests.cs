using AqwCompanion.App.Skua;
using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Skua;

public class SkuaSettingsPresenterTests
{
    private sealed class Feed : ISkuaFeed
    {
        public SkuaView Current { get; set; } = SkuaView.Empty;

        public int Handlers { get; private set; }

        private EventHandler? _changed;

        public event EventHandler? Changed
        {
            add { _changed += value; Handlers++; }
            remove { _changed -= value; Handlers--; }
        }

        public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
    }

    private static SettingsViewModel Vm() => new(new FakeAppSettingsStore(), new ForgetCoordinator(() => true, () => []));

    [Fact]
    public void Start_shows_the_current_state_and_changes_go_through_the_ui_thread()
    {
        var feed = new Feed();
        var vm = Vm();
        var queue = new Queue<Action>();
        using var presenter = new SkuaSettingsPresenter(feed, vm, queue.Enqueue);

        presenter.Start();
        Assert.Single(queue);
        queue.Dequeue()();
        Assert.Equal(SkuaStatusText.For(SkuaView.Empty), vm.SkuaStatusText);

        feed.Current = new SkuaView(SkuaConnection.VersionMismatch, null, null, []);
        feed.Raise();
        Assert.Equal(SkuaStatusText.For(SkuaView.Empty), vm.SkuaStatusText);
        queue.Dequeue()();
        Assert.Equal(SkuaStatusText.For(feed.Current), vm.SkuaStatusText);
    }

    [Fact]
    public void Dispose_unsubscribes_and_drops_queued_work()
    {
        var feed = new Feed();
        var vm = Vm();
        var queue = new Queue<Action>();
        var presenter = new SkuaSettingsPresenter(feed, vm, queue.Enqueue);
        presenter.Start();
        presenter.Start();
        Assert.Equal(1, feed.Handlers);
        var before = vm.SkuaStatusText;

        presenter.Dispose();
        feed.Current = new SkuaView(SkuaConnection.VersionMismatch, null, null, []);
        while (queue.Count > 0)
        {
            queue.Dequeue()();
        }

        Assert.Equal(0, feed.Handlers);
        Assert.Equal(before, vm.SkuaStatusText);
    }
}
