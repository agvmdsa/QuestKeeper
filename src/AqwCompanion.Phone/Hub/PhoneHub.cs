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
    private readonly object _lock = new();
    private readonly List<HubSubscription> _subscribers = [];
    private FeedSnapshot _snapshot;
    private string _snapshotJson;

    public PhoneHub(IFeedSource source, NoticeEngine engine, NoticeLog log, Func<DateTimeOffset> now)
    {
        _source = source;
        _engine = engine;
        _log = log;
        _now = now;
        _snapshot = source.Current;
        _snapshotJson = PhoneJson.SerializeSnapshot(_snapshot);
        _engine.Process(_snapshot, _now());   // the baseline: nothing existing is announced
        source.Changed += OnSourceChanged;
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
            PublishNotices(_engine.Tick(_now()));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _engine.Reset();
            _log.Clear();
            _snapshot = _source.Current;
            _snapshotJson = PhoneJson.SerializeSnapshot(_snapshot);
            _engine.Process(_snapshot, _now());
            PublishState();
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
        lock (_lock)
        {
            var snapshot = _source.Current;
            var json = PhoneJson.SerializeSnapshot(snapshot);
            if (json == _snapshotJson)
            {
                return;
            }

            _snapshot = snapshot;
            _snapshotJson = json;
            PublishNotices(_engine.Process(snapshot, _now()));
            PublishState();
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
