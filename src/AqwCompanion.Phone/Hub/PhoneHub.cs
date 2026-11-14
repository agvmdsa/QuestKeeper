using System.Threading.Channels;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.Phone.Hub;

public sealed record HubMessage(string Event, string Json);

public sealed class HubSubscription : IDisposable
{
    private readonly PhoneHub _hub;
    private readonly Channel<HubMessage> _channel = Channel.CreateBounded<HubMessage>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    internal HubSubscription(PhoneHub hub) => _hub = hub;

    public ChannelReader<HubMessage> Reader => _channel.Reader;

    internal void Offer(HubMessage message) => _channel.Writer.TryWrite(message);

    public void Dispose()
    {
        _hub.Remove(this);
        _channel.Writer.TryComplete();
    }
}

/// <summary>The latest phone state and the fan-out to every connected phone. Each change is serialized once. Safe from any thread.</summary>
public sealed class PhoneHub : IDisposable
{
    private readonly IFeedSource _source;
    private readonly NoticeEngine _engine;
    private readonly NoticeLog _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Exception>? _onError;
    private readonly ISkuaFeed? _skua;
    private readonly object _lock = new();
    private readonly List<HubSubscription> _subscribers = [];
    private readonly Action<TimeSpan, Action>? _schedule;
    private FeedSnapshot _snapshot;
    private string _snapshotJson;
    private bool _checkScheduled;

    /// <param name="schedule">
    /// Runs an action once after a delay; used to look again when a Skua waits for its options window (a notice is due
    /// after <see cref="NoticeEngine.OptionsWaitingAfter"/>, sooner than the regular <see cref="Tick"/>). Null: only
    /// <see cref="Tick"/> looks.
    /// </param>
    public PhoneHub(
        IFeedSource source, NoticeEngine engine, NoticeLog log, Func<DateTimeOffset> now, Action<Exception>? onError = null, ISkuaFeed? skua = null,
        Action<TimeSpan, Action>? schedule = null)
    {
        _schedule = schedule;
        _source = source;
        _engine = engine;
        _log = log;
        _now = now;
        _onError = onError;
        _skua = skua;
        if (skua is not null)
        {
            skua.Changed += OnSkuaChanged;   // before any read of Current: a change in between is not lost
        }

        source.Changed += OnSourceChanged;   // before the read: a change in between is not lost, an equal one is skipped
        lock (_lock)
        {
            _snapshot = source.Current;
            _snapshotJson = PhoneJson.SerializeSnapshot(_snapshot);
            _engine.Process(_snapshot, _now());   // the baseline: nothing existing is announced
            SkuaBaseline();
        }
    }

    public PhoneState Current
    {
        get
        {
            lock (_lock)
            {
                return State(_skua?.Current);
            }
        }
    }

    public string CurrentJson => PhoneJson.Serialize(Current);

    public int SubscriberCount
    {
        get
        {
            lock (_lock)
            {
                return _subscribers.Count;
            }
        }
    }

    public HubSubscription Subscribe()
    {
        var subscription = new HubSubscription(this);
        lock (_lock)
        {
            _subscribers.Add(subscription);
        }

        return subscription;
    }

    public void Tick()
    {
        lock (_lock)
        {
            try
            {
                PublishNotices(_engine.Tick(_now()));
                ScheduleOptionsCheck();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            try
            {
                var snapshot = _source.Current;
                var json = PhoneJson.SerializeSnapshot(snapshot);
                _engine.Reset();
                _log.Clear();
                _engine.Process(snapshot, _now());
                SkuaBaseline();
                _snapshot = snapshot;
                _snapshotJson = json;
                PublishState();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }
    }

    public void Dispose()
    {
        _source.Changed -= OnSourceChanged;
        if (_skua is not null)
        {
            _skua.Changed -= OnSkuaChanged;
        }

        lock (_lock)
        {
            foreach (var subscriber in _subscribers.ToList())
            {
                subscriber.Dispose();
            }
        }
    }

    internal void Remove(HubSubscription subscription)
    {
        lock (_lock)
        {
            _subscribers.Remove(subscription);
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        // Runs on the raiser's thread (capture session, UI): nothing may escape.
        try
        {
            lock (_lock)
            {
                var snapshot = _source.Current;
                var json = PhoneJson.SerializeSnapshot(snapshot);
                if (json == _snapshotJson)
                {
                    return;
                }

                var notices = _engine.Process(snapshot, _now());
                _snapshot = snapshot;
                _snapshotJson = json;
                PublishNotices(notices);
                PublishState();
            }
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void OnSkuaChanged(object? sender, EventArgs e)
    {
        // Runs on the raiser's thread: nothing may escape. The state is rebuilt from the feed's current view, read once.
        try
        {
            lock (_lock)
            {
                var view = _skua!.Current;
                PublishNotices(_engine.ProcessSkua(view, _now()));
                PublishState(view);
                ScheduleOptionsCheck();
            }
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    /// <summary>Under the lock: asks for one look when a Skua's options wait becomes due (at most one look pending).</summary>
    private void ScheduleOptionsCheck()
    {
        if (_schedule is null || _checkScheduled || _engine.SkuaOptionsDueIn(_now()) is not { } due)
        {
            return;
        }

        _checkScheduled = true;
        _schedule(due + TimeSpan.FromMilliseconds(100), OptionsCheck);
    }

    private void OptionsCheck()
    {
        lock (_lock)
        {
            _checkScheduled = false;
        }

        Tick();
    }

    private void Report(Exception ex)
    {
        try
        {
            _onError?.Invoke(ex);
        }
        catch
        {
            // The reporter must not throw into the raiser either.
        }
    }

    private void PublishNotices(IReadOnlyList<Notice> notices)
    {
        foreach (var notice in notices)
        {
            _log.Add(notice);
            Publish(new HubMessage("notice", PhoneJson.SerializeNotice(notice)));
        }
    }

    private void PublishState() => PublishState(_skua?.Current);

    private void PublishState(SkuaHubView? skua) => Publish(new HubMessage("state", PhoneJson.Serialize(State(skua))));

    /// <summary>Under the lock.</summary>
    private PhoneState State(SkuaHubView? skua) =>
        new(_snapshot, _log.Recent, skua?.Primary, skua is null ? null : SkuaInstanceState.From(skua));

    /// <summary>Under the lock: the Skua instances as they are now raise no notice (a Skua already disconnected is not announced).</summary>
    private void SkuaBaseline()
    {
        try
        {
            if (_skua is not null)
            {
                _engine.ProcessSkua(_skua.Current, _now());
            }
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void Publish(HubMessage message)
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber.Offer(message);
        }
    }
}
