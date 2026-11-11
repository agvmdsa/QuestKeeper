using System.Threading.Channels;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Notices;

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
    private readonly object _lock = new();
    private readonly List<HubSubscription> _subscribers = [];
    private FeedSnapshot _snapshot;
    private string _snapshotJson;

    public PhoneHub(IFeedSource source, NoticeEngine engine, NoticeLog log, Func<DateTimeOffset> now, Action<Exception>? onError = null)
    {
        _source = source;
        _engine = engine;
        _log = log;
        _now = now;
        _onError = onError;
        source.Changed += OnSourceChanged;   // before the read: a change in between is not lost, an equal one is skipped
        lock (_lock)
        {
            _snapshot = source.Current;
            _snapshotJson = PhoneJson.SerializeSnapshot(_snapshot);
            _engine.Process(_snapshot, _now());   // the baseline: nothing existing is announced
        }
    }

    public PhoneState Current
    {
        get
        {
            lock (_lock)
            {
                return new PhoneState(_snapshot, _log.Recent);
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

    private void PublishState() => Publish(new HubMessage("state", PhoneJson.Serialize(new PhoneState(_snapshot, _log.Recent))));

    private void Publish(HubMessage message)
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber.Offer(message);
        }
    }
}
