using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaBridgeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeStore _store = new();
    private DateTimeOffset _now = T0;

    private SkuaBridge Create() => new(_store, () => _now);

    private static LogMessage Entry(int i, DateTimeOffset? at = null) =>
        new(new SkuaLogEntry(at ?? T0.AddSeconds(i), SkuaLogKind.Dropped, "drop " + i));

    [Fact]
    public void A_new_bridge_is_not_connected_and_loads_the_stored_log_newest_first()
    {
        Assert.Empty(Create().Log);
        _store.Stored = [new(T0, SkuaLogKind.Bought, "old"), new(T0.AddMinutes(1), SkuaLogKind.Sold, "new")];

        var bridge = Create();

        Assert.Equal(SkuaConnection.NotConnected, bridge.Current.Connection);
        Assert.Null(bridge.Current.Hello);
        Assert.Null(bridge.Current.Status);
        Assert.Equal(["new", "old"], bridge.Log.Select(e => e.Text));
    }

    [Fact]
    public void Connect_hello_and_disconnect_move_the_state_and_raise_changed_once_each()
    {
        var bridge = Create();
        var changes = 0;
        bridge.Changed += (_, _) => changes++;
        var status = new SkuaStatus(true, "Hero", "Safiria", "battleon", null, false, null);

        bridge.ClientConnected();
        Assert.Equal(SkuaConnection.Connected, bridge.Current.Connection);
        Assert.Equal(1, changes);

        var hello = new SkuaHello("0.1.0", "1.4.5.0");
        bridge.Accept(new HelloMessage(hello));
        Assert.Equal(hello, bridge.Current.Hello);
        Assert.Equal(2, changes);

        bridge.Accept(new StatusMessage(status));
        bridge.Accept(Entry(1));
        changes = 0;
        bridge.ClientDisconnected();

        Assert.Equal(1, changes);
        Assert.Equal(SkuaConnection.NotConnected, bridge.Current.Connection);
        Assert.Equal(status, bridge.Current.Status);
        Assert.Single(bridge.Log);
    }

    [Fact]
    public void Status_replaces_and_log_adds_at_the_front_each_raising_changed()
    {
        var bridge = Create();
        var changes = 0;
        bridge.Changed += (_, _) => changes++;

        bridge.Accept(new StatusMessage(new SkuaStatus(true, "A", null, null, null, false, null)));
        var second = new SkuaStatus(true, "B", null, null, null, false, null);
        bridge.Accept(new StatusMessage(second));
        bridge.Accept(Entry(1));
        bridge.Accept(Entry(2));

        Assert.Equal(4, changes);
        Assert.Equal(second, bridge.Current.Status);
        Assert.Equal(["drop 2", "drop 1"], bridge.Log.Select(e => e.Text));
    }

    [Fact]
    public void The_log_never_holds_more_than_500()
    {
        var bridge = Create();
        for (var i = 0; i < 501; i++)
        {
            bridge.Accept(Entry(i));
        }

        Assert.Equal(500, bridge.Log.Count);
        Assert.Equal("drop 500", bridge.Log[0].Text);
        Assert.DoesNotContain(bridge.Log, e => e.Text == "drop 0");
        Assert.Equal("drop 1", bridge.Log[^1].Text);
    }

    [Fact]
    public void The_view_log_has_at_most_100_entries_newest_first()
    {
        var bridge = Create();
        for (var i = 0; i < 150; i++)
        {
            bridge.Accept(Entry(i));
        }

        Assert.Equal(100, bridge.Current.Log.Count);
        Assert.Equal("drop 149", bridge.Current.Log[0].Text);
    }

    [Fact]
    public void An_unsupported_version_sets_mismatch_adds_nothing_and_clears_on_reconnect()
    {
        var bridge = Create();
        bridge.ClientConnected();

        bridge.Accept(new UnsupportedVersionMessage(9));

        Assert.Equal(SkuaConnection.VersionMismatch, bridge.Current.Connection);
        Assert.Empty(bridge.Log);
        bridge.Accept(new HelloMessage(new SkuaHello("0.1.0", "1.4.5.0")));
        Assert.Equal(SkuaConnection.VersionMismatch, bridge.Current.Connection);
        bridge.ClientDisconnected();
        Assert.Equal(SkuaConnection.NotConnected, bridge.Current.Connection);
        bridge.ClientConnected();
        Assert.Equal(SkuaConnection.Connected, bridge.Current.Connection);
    }

    [Fact]
    public void Reject_counts_without_raising_changed()
    {
        var bridge = Create();
        var changes = 0;
        bridge.Changed += (_, _) => changes++;

        bridge.Reject();
        bridge.Reject();

        Assert.Equal(2, bridge.Rejected);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Persistence_is_throttled_to_one_save_per_ten_seconds()
    {
        var bridge = Create();

        bridge.Accept(Entry(1));
        Assert.Equal(1, _store.Saves);

        _now = T0.AddSeconds(9);
        bridge.Accept(Entry(2));
        Assert.Equal(1, _store.Saves);

        _now = T0.AddSeconds(10);
        bridge.Accept(Entry(3));
        Assert.Equal(2, _store.Saves);
        Assert.Equal(3, _store.Stored.Count);

        _now = T0.AddSeconds(11);
        bridge.Accept(Entry(4));
        Assert.Equal(2, _store.Saves);

        bridge.Flush();
        Assert.Equal(3, _store.Saves);
        Assert.Equal(4, _store.Stored.Count);

        bridge.Flush();
        Assert.Equal(3, _store.Saves);
    }

    [Fact]
    public void Disconnect_flushes_unsaved_entries()
    {
        var bridge = Create();
        bridge.Accept(Entry(1));
        _now = T0.AddSeconds(1);
        bridge.Accept(Entry(2));
        Assert.Equal(1, _store.Saves);

        bridge.ClientDisconnected();

        Assert.Equal(2, _store.Saves);
        Assert.Equal(2, _store.Stored.Count);
    }

    [Fact]
    public void Quiesce_ignores_messages_and_resume_accepts_them_again()
    {
        var bridge = Create();
        var changes = 0;
        bridge.Changed += (_, _) => changes++;

        bridge.Quiesce();
        bridge.Accept(Entry(1));
        bridge.Accept(new StatusMessage(new SkuaStatus(true, null, null, null, null, false, null)));

        Assert.Equal(0, changes);
        Assert.Empty(bridge.Log);
        Assert.Null(bridge.Current.Status);

        bridge.Resume();
        bridge.Accept(Entry(2));

        Assert.Equal(1, changes);
        Assert.Single(bridge.Log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Forget_clears_the_log_and_status_deletes_the_file_and_keeps_the_hello_and_connection(bool quiesceFirst)
    {
        var bridge = Create();
        bridge.ClientConnected();
        var hello = new SkuaHello("0.1.0", "1.4.5.0");
        bridge.Accept(new HelloMessage(hello));
        bridge.Accept(new StatusMessage(new SkuaStatus(true, "A", null, null, null, false, null)));
        bridge.Accept(Entry(1));
        var changes = 0;
        var forgotten = 0;
        bridge.Changed += (_, _) => changes++;
        bridge.Forgotten += (_, _) => forgotten++;

        if (quiesceFirst)
        {
            bridge.Quiesce();
        }

        bridge.Forget();

        Assert.Empty(bridge.Log);
        Assert.Equal(hello, bridge.Current.Hello);
        Assert.Null(bridge.Current.Status);
        Assert.Equal(SkuaConnection.Connected, bridge.Current.Connection);
        Assert.Equal(1, _store.Deletes);
        Assert.Equal(1, changes);
        Assert.Equal(1, forgotten);
        bridge.Flush();
        Assert.Equal(1, _store.Saves);
    }

    [Fact]
    public void After_quiesce_and_a_successful_forget_messages_are_accepted_again()
    {
        var bridge = Create();
        bridge.ClientConnected();
        bridge.Quiesce();
        bridge.Forget();
        var changes = 0;
        bridge.Changed += (_, _) => changes++;

        var hello = new SkuaHello("0.1.0", "1.4.5.0");
        var status = new SkuaStatus(true, "Hero", null, "battleon", null, false, null);
        bridge.Accept(new HelloMessage(hello));
        bridge.Accept(new StatusMessage(status));
        bridge.Accept(Entry(1));

        Assert.Equal(3, changes);
        Assert.Equal(hello, bridge.Current.Hello);
        Assert.Equal(status, bridge.Current.Status);
        Assert.Equal(["drop 1"], bridge.Log.Select(e => e.Text));
        Assert.Equal(["drop 1"], _store.Stored.Select(e => e.Text));
    }

    [Fact]
    public async Task An_entry_accepted_during_a_forget_without_quiesce_is_not_deleted_from_the_store()
    {
        var bridge = Create();
        bridge.Accept(Entry(1));
        Assert.Equal(["drop 1"], _store.Stored.Select(e => e.Text));
        Task? writer = null;
        var finishedEarly = false;
        // Runs between the in-memory clear and the delete. With the I/O lock held across both, the new entry's save waits for
        // the delete; without it the save would land first and the delete would remove the entry from the store.
        bridge.AfterClear = () =>
        {
            writer = Task.Run(() => bridge.Accept(Entry(2)));
            finishedEarly = writer.Wait(TimeSpan.FromMilliseconds(300));
        };

        bridge.Forget();
        await writer!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(finishedEarly);
        Assert.Equal(["drop 2"], bridge.Log.Select(e => e.Text));
        Assert.Equal(["drop 2"], _store.Stored.Select(e => e.Text));
    }

    [Fact]
    public void The_name_is_skua_log()
    {
        Assert.Equal("Skua log", Create().Name);
    }

    [Fact]
    public void Four_threads_accepting_200_entries_each_leave_500()
    {
        var bridge = Create();
        var tasks = Enumerable.Range(0, 4)
            .Select(t => Task.Run(() =>
            {
                for (var i = 0; i < 200; i++)
                {
                    bridge.Accept(Entry(t * 1000 + i));
                    _ = bridge.Current;
                }
            }))
            .ToArray();

        Assert.Null(Record.Exception(() => Task.WaitAll(tasks)));
        Assert.Equal(500, bridge.Log.Count);
    }

    [Fact]
    public void A_throwing_changed_handler_does_not_stop_the_others_or_escape()
    {
        var bridge = Create();
        var reached = 0;
        bridge.Changed += (_, _) => throw new InvalidOperationException("boom");
        bridge.Changed += (_, _) => reached++;

        Assert.Null(Record.Exception(() => bridge.Accept(Entry(1))));
        Assert.Null(Record.Exception(bridge.ClientConnected));

        Assert.Equal(2, reached);
    }

    [Fact]
    public void The_first_entry_after_a_forget_saves_at_once()
    {
        var bridge = Create();
        bridge.Accept(Entry(1));
        bridge.Forget();
        Assert.Equal(1, _store.Saves);

        bridge.Accept(Entry(2));

        Assert.Equal(2, _store.Saves);
    }

    [Fact]
    public async Task A_forget_waits_for_a_save_in_flight_and_the_file_does_not_reappear()
    {
        var gate = new ManualResetEventSlim();
        var store = new BlockingStore(gate);
        var bridge = new SkuaBridge(store, () => _now);
        var writer = Task.Run(() => bridge.Accept(Entry(1)));
        Assert.True(store.SaveEntered.Wait(TimeSpan.FromSeconds(10)));

        var forgetter = Task.Run(bridge.Forget);
        // Without the I/O lock the delete would run now; with it Forget is still waiting for the save.
        var deletedEarly = store.DeleteCalled.Wait(TimeSpan.FromMilliseconds(300));
        gate.Set();
        await Task.WhenAll(writer, forgetter).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(deletedEarly);
        Assert.Empty(store.Stored);
    }

    [Fact]
    public async Task A_save_that_loses_the_race_with_a_forget_is_suppressed()
    {
        var bridge = Create();
        var entered = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        bridge.BeforeWrite = () =>
        {
            entered.Set();
            gate.Wait();
        };
        var writer = Task.Run(() => bridge.Accept(Entry(1)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        bridge.Forget();
        gate.Set();
        await writer.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, _store.Saves);
        Assert.Empty(_store.Stored);
    }

    [Fact]
    public async Task An_older_snapshot_arriving_after_a_newer_one_is_skipped()
    {
        var bridge = Create();
        var entered = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        var calls = 0;
        bridge.BeforeWrite = () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                gate.Wait();
            }
        };
        var writer = Task.Run(() => bridge.Accept(Entry(1)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        bridge.Accept(Entry(2));
        bridge.Flush();
        Assert.Equal(2, _store.Stored.Count);
        gate.Set();
        await writer.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, _store.Saves);
        Assert.Equal(2, _store.Stored.Count);
        bridge.Flush();
        Assert.Equal(1, _store.Saves);
    }

    private sealed class BlockingStore(ManualResetEventSlim gate) : ISkuaLogStore
    {
        public ManualResetEventSlim SaveEntered { get; } = new();

        public ManualResetEventSlim DeleteCalled { get; } = new();

        public List<SkuaLogEntry> Stored { get; private set; } = [];

        public IReadOnlyList<SkuaLogEntry> Load() => [];

        public void Save(IReadOnlyList<SkuaLogEntry> log)
        {
            SaveEntered.Set();
            gate.Wait();
            Stored = [.. log];
        }

        public void Delete()
        {
            DeleteCalled.Set();
            Stored = [];
        }
    }

    private sealed class FakeStore : ISkuaLogStore
    {
        public List<SkuaLogEntry> Stored { get; set; } = [];

        public int Saves { get; private set; }

        public int Deletes { get; private set; }

        public IReadOnlyList<SkuaLogEntry> Load() => Stored;

        public void Save(IReadOnlyList<SkuaLogEntry> log)
        {
            Saves++;
            Stored = [.. log];
        }

        public void Delete()
        {
            Deletes++;
            Stored = [];
        }
    }
}
