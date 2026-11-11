using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using AqwCompanion.Skua;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

namespace QuestKeeper.SkuaBridge;

/// <summary>
/// Read-only adapter inside Skua. It only reads properties and subscribes to events, and reports what it sees to
/// Questkeeper over the outbound pipe. It never calls a Skua method that changes game or Skua state, receives nothing
/// from Questkeeper, and never lets an exception reach Skua. See README.md for the audited list of Skua members.
/// </summary>
public sealed class SkuaBridgePlugin : ISkuaPlugin
{
    /// <summary>How often the status is re-read. Skua raises its game events only while a script runs, so a login, logout or map change by hand has no event.</summary>
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromSeconds(3);

    /// <summary>A purchase is resolved against the inventory this long after the event, so the game has applied it first.</summary>
    private static readonly TimeSpan BoughtSettle = TimeSpan.FromMilliseconds(300);

    private const int WorkCapacity = 256;

    private readonly object _subscriptionLock = new();
    private readonly object _stateLock = new();
    private readonly SkuaItemNames _names = new();

    private IScriptInterface? _bot;
    private INotifyPropertyChanged? _managerChanges;
    private PipeClient? _client;
    private Channel<WorkItem>? _work;
    private CancellationTokenSource? _stop;
    private bool _subscribed;
    private bool _unloaded;

    // State owned by the event side (under _stateLock).
    private bool _running;
    private DateTimeOffset? _runningSince;

    // State owned by the worker task only.
    private long _lastSnapshotStart = long.MinValue;
    private long _lastStatusStart = long.MinValue;
    private SkuaStatus? _lastStatus;
    private bool _wasLoggedIn;

    // Handlers kept in fields so Unload removes exactly what Load added.
    private readonly ItemBoughtHandler _onBought;
    private readonly ItemSoldHandler _onSold;
    private readonly ItemDroppedHandler _onDropped;
    private readonly MapChangedEventHandler _onMapChanged;
    private readonly QuestAcceptedEventHandler _onQuestAccepted;
    private readonly QuestTurnInEventHandler _onQuestTurnedIn;
    private readonly PlayerDeathEventHandler _onDeath;
    private readonly LogoutEventHandler _onLogout;
    private readonly ReloginTriggeredEventHandler _onRelogin;
    private readonly ScriptStoppingHandler _onScriptStopping;
    private readonly PropertyChangedEventHandler _onManagerChanged;

    public SkuaBridgePlugin()
    {
        _onBought = id => Safe(() => Bought(id));
        _onSold = (id, quantitySold, _, cost, isAc) => Safe(() => Sold(id, quantitySold, cost, isAc));
        _onDropped = (item, addedToInv, _) => Safe(() => Dropped(item, addedToInv));
        _onMapChanged = map => Safe(() => MapChanged(map));
        _onQuestAccepted = id => Safe(() => Quest(id, accepted: true));
        _onQuestTurnedIn = id => Safe(() => Quest(id, accepted: false));
        _onDeath = () => Safe(() => Log(SkuaLogFormatter.Died(Now())));
        _onLogout = () => Safe(() =>
        {
            Log(SkuaLogFormatter.LoggedOut(Now()));
            RequestStatus();
        });
        _onRelogin = kicked => Safe(() =>
        {
            Log(SkuaLogFormatter.Relogin(Now(), kicked));
            RequestStatus();
        });
        _onScriptStopping = ScriptStopping;
        _onManagerChanged = (_, e) => Safe(() => ManagerChanged(e.PropertyName));
    }

    public string Name => "QuestKeeper Bridge";
    public string Author => "QuestKeeper";
    public string Description => "Shows what Skua is doing in Questkeeper. Read only.";
    public List<IOption>? Options => null;

    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
        try
        {
            if (provider?.GetService(typeof(IScriptInterface)) is not IScriptInterface bot)
            {
                return;
            }

            _bot = bot;
            _managerChanges = provider.GetService(typeof(IScriptManager)) as INotifyPropertyChanged ?? bot.Manager as INotifyPropertyChanged;
            lock (_stateLock)
            {
                // Plugins load at Skua start, before any script. If one is already running its start time is unknown.
                _running = bot.Manager.ScriptRunning;
                _runningSince = null;
            }

            var skuaVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            _client = new PipeClient(new SkuaHello(BridgeMessages.PluginVersion(typeof(SkuaBridgePlugin).Assembly), skuaVersion));
            _work = Channel.CreateBounded<WorkItem>(
                new BoundedChannelOptions(WorkCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
            _stop = new CancellationTokenSource();

            Subscribe();
            _client.Start();
            var stop = _stop.Token;
            _ = Task.Run(() => WorkAsync(stop), stop);
            RequestStatus();
            RequestSnapshot();
        }
        catch (Exception)
        {
            // A failed Load leaves Skua as it was: nothing subscribed, nothing running.
            Unload();
        }
    }

    public void Unload()
    {
        try
        {
            lock (_subscriptionLock)
            {
                _unloaded = true;
                Unsubscribe();
            }

            _work?.Writer.TryComplete();
            _stop?.Cancel();
            _client?.Dispose();
        }
        catch (Exception)
        {
            // Unloading must never fail into Skua.
        }
    }

    // ---- Subscriptions -------------------------------------------------------------------------------------------

    private void Subscribe()
    {
        lock (_subscriptionLock)
        {
            if (_unloaded || _bot is null)
            {
                return;
            }

            _subscribed = true;
            try
            {
                AddEvents(_bot.Events);
                if (_managerChanges is not null)
                {
                    _managerChanges.PropertyChanged += _onManagerChanged;
                }
            }
            catch (Exception)
            {
                Unsubscribe();
                throw;
            }
        }
    }

    /// <summary>
    /// Skua's ScriptEvent.ClearHandlers (run on every ScriptStoppedMessage) sets most events to null, which drops this
    /// plugin's handlers too. They are put back on each running change: removing first keeps exactly one copy.
    /// </summary>
    private void Resubscribe()
    {
        lock (_subscriptionLock)
        {
            if (_unloaded || !_subscribed || _bot is null)
            {
                return;
            }

            var events = _bot.Events;
            RemoveEvents(events);
            AddEvents(events);
        }
    }

    private void Unsubscribe()
    {
        // Every removal on its own: one failure must not leave the others attached. Removing a handler that is not attached does nothing.
        if (_bot is { } bot)
        {
            Safe(() => RemoveEvents(bot.Events));
        }

        if (_managerChanges is { } changes)
        {
            Safe(() => changes.PropertyChanged -= _onManagerChanged);
        }

        _subscribed = false;
    }

    private void AddEvents(IScriptEvent events)
    {
        events.ItemBought += _onBought;
        events.ItemSold += _onSold;
        events.ItemDropped += _onDropped;
        events.MapChanged += _onMapChanged;
        events.QuestAccepted += _onQuestAccepted;
        events.QuestTurnedIn += _onQuestTurnedIn;
        events.PlayerDeath += _onDeath;
        events.Logout += _onLogout;
        events.ReloginTriggered += _onRelogin;
        events.ScriptStopping += _onScriptStopping;
    }

    private void RemoveEvents(IScriptEvent events)
    {
        Safe(() => events.ItemBought -= _onBought);
        Safe(() => events.ItemSold -= _onSold);
        Safe(() => events.ItemDropped -= _onDropped);
        Safe(() => events.MapChanged -= _onMapChanged);
        Safe(() => events.QuestAccepted -= _onQuestAccepted);
        Safe(() => events.QuestTurnedIn -= _onQuestTurnedIn);
        Safe(() => events.PlayerDeath -= _onDeath);
        Safe(() => events.Logout -= _onLogout);
        Safe(() => events.ReloginTriggered -= _onRelogin);
        Safe(() => events.ScriptStopping -= _onScriptStopping);
    }

    // ---- Event handlers (Skua's threads: cheap, never block, never throw) -----------------------------------------

    private void Bought(int charItemId)
    {
        var at = Now();
        Post(() =>
        {
            var before = _names.QuantityOf(charItemId);
            var items = ReadInventory();
            var item = items?.Find(i => i.CharItemID == charItemId);
            var name = item?.Name ?? _names.NameOf(charItemId);
            Log(SkuaLogFormatter.Bought(at, name, charItemId, BridgeMessages.BoughtQuantity(before, item?.Quantity)));
            if (items is not null)
            {
                ReplaceSnapshot(items);
            }
        }, delay: BoughtSettle);
    }

    private void Sold(int charItemId, int quantitySold, int cost, bool isAc)
    {
        // The item may already be gone from the inventory: the name comes from the snapshot taken before the sale.
        Log(SkuaLogFormatter.Sold(Now(), _names.NameOf(charItemId), charItemId, quantitySold, cost, isAc));
        RequestSnapshot();
    }

    private void Dropped(ItemBase? item, bool addedToInv)
    {
        Log(SkuaLogFormatter.Dropped(Now(), item?.Name, addedToInv));
        if (addedToInv)
        {
            RequestSnapshot();
        }
    }

    private void MapChanged(string? map)
    {
        if (!string.IsNullOrWhiteSpace(map))
        {
            Log(SkuaLogFormatter.MapChanged(Now(), map));
        }

        RequestStatus();
        RequestSnapshot();
    }

    private void Quest(int questId, bool accepted)
    {
        var at = Now();
        Post(() =>
        {
            var name = ReadQuestName(questId);
            Log(accepted ? SkuaLogFormatter.QuestAccepted(at, questId, name) : SkuaLogFormatter.QuestTurnedIn(at, questId, name));
        });
    }

    /// <summary>
    /// Skua uses the value of the last handler only for a diagnostic trace line ("finished successfully" / "finished
    /// early or with errors"), nothing else. True is the delegate's documented "the script should be stopped", which is
    /// what happens anyway; since this handler is attached when the script starts, a script's own handler runs after
    /// it and its value is the one Skua sees.
    /// </summary>
    private bool ScriptStopping(Exception? exception)
    {
        if (exception is not null)
        {
            Safe(() => Log(SkuaLogFormatter.ScriptError(Now(), BridgeMessages.ErrorText(exception))));
        }

        return true;
    }

    private void ManagerChanged(string? propertyName)
    {
        var all = string.IsNullOrEmpty(propertyName);
        if (all || propertyName == nameof(IScriptStatus.ScriptRunning))
        {
            RunningChanged(_bot?.Manager.ScriptRunning ?? false);
        }

        if (all || propertyName == nameof(IScriptStatus.LoadedScript))
        {
            RequestStatus();
        }
    }

    /// <summary>Skua raises ScriptRunning more than once per change (StopScript raises it again), so only real transitions count.</summary>
    private void RunningChanged(bool running)
    {
        var at = Now();
        lock (_stateLock)
        {
            if (running == _running)
            {
                return;
            }

            _running = running;
            _runningSince = running ? at : null;
        }

        Resubscribe();
        var script = BridgeMessages.ScriptName(_bot?.Manager.LoadedScript) ?? "unknown script";
        Log(running ? SkuaLogFormatter.ScriptStarted(at, script) : SkuaLogFormatter.ScriptStopped(at, script));
        RequestStatus();
        if (running)
        {
            RequestSnapshot();
        }
    }

    // ---- Worker (one background task: all inventory, quest and status reads happen here, never on Skua's threads) --

    private void Post(Action run, TimeSpan delay = default)
    {
        _work?.Writer.TryWrite(new WorkItem(Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency), run));
    }

    /// <summary>Skipped when a snapshot was started after the request: that one already saw the state the request was about.</summary>
    private void RequestSnapshot()
    {
        var requested = Stopwatch.GetTimestamp();
        Post(() =>
        {
            if (_lastSnapshotStart < requested && ReadInventory() is { } items)
            {
                ReplaceSnapshot(items);
            }
        });
    }

    private void RequestStatus()
    {
        var requested = Stopwatch.GetTimestamp();
        Post(() =>
        {
            if (_lastStatusStart < requested)
            {
                PublishStatus();
            }
        });
    }

    private async Task WorkAsync(CancellationToken ct)
    {
        var reader = _work!.Reader;
        var nextPoll = Stopwatch.GetTimestamp();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var untilPoll = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), nextPoll);
                if (untilPoll > TimeSpan.Zero)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(untilPoll);
                    try
                    {
                        if (!await reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false))
                        {
                            return;
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Poll time.
                    }
                }

                while (!ct.IsCancellationRequested && reader.TryRead(out var item))
                {
                    var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), item.NotBefore);
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, ct).ConfigureAwait(false);
                    }

                    Safe(item.Run);
                }

                if (Stopwatch.GetTimestamp() >= nextPoll)
                {
                    nextPoll = Stopwatch.GetTimestamp() + (long)(StatusPollInterval.TotalSeconds * Stopwatch.Frequency);
                    Safe(PublishStatus);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // The worker never stops on an error; the next item or poll tries again.
            }
        }
    }

    private void PublishStatus()
    {
        if (_bot is not { } bot || Volatile.Read(ref _unloaded))
        {
            return;
        }

        _lastStatusStart = Stopwatch.GetTimestamp();
        var loggedIn = bot.Player.LoggedIn;
        bool running;
        DateTimeOffset? since;
        lock (_stateLock)
        {
            running = _running;
            since = _runningSince;
        }

        var status = BridgeMessages.Status(
            loggedIn,
            loggedIn ? bot.Player.Username : null,
            loggedIn ? bot.Servers.LastName : null,
            loggedIn ? bot.Map.Name : null,
            bot.Manager.LoadedScript,
            running,
            since);

        if (status != _lastStatus)
        {
            _lastStatus = status;
            _client?.Send(new StatusMessage(status));
        }

        // A login by hand raises no event: take the name snapshot when one is seen.
        if (loggedIn && !_wasLoggedIn)
        {
            RequestSnapshot();
        }

        _wasLoggedIn = loggedIn;
    }

    /// <summary>The inventory, or null when it cannot be read (not logged in, game not loaded, or Skua answered with nothing).</summary>
    private List<InventoryItem>? ReadInventory()
    {
        if (_bot is not { } bot || Volatile.Read(ref _unloaded))
        {
            return null;
        }

        _lastSnapshotStart = Stopwatch.GetTimestamp();
        if (!bot.Player.LoggedIn)
        {
            return null;
        }

        // Skua returns a fresh list per read; copy defensively anyway so nothing here touches a list Skua may reuse.
        var items = bot.Inventory.Items;
        return items is { Count: > 0 } ? [.. items] : null;
    }

    private void ReplaceSnapshot(List<InventoryItem> items)
    {
        _names.Replace(items.Where(i => i is not null).Select(i => (i.CharItemID, i.Name ?? string.Empty, i.Quantity)));
    }

    private string? ReadQuestName(int questId)
    {
        if (_bot is not { } bot || Volatile.Read(ref _unloaded))
        {
            return null;
        }

        var tree = bot.Quests.Tree;
        return tree?.Find(q => q is not null && q.ID == questId)?.Name;
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------

    private void Log(SkuaLogEntry entry) => _client?.Send(new LogMessage(entry));

    private static DateTimeOffset Now() => DateTimeOffset.UtcNow;

    /// <summary>Every handler runs through here: nothing the plugin does may throw into Skua.</summary>
    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Swallowed by design: a reporting failure must never disturb Skua or a running script.
        }
    }

    private readonly record struct WorkItem(long NotBefore, Action Run);
}
