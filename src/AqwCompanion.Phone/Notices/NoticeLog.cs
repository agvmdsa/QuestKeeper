namespace AqwCompanion.Phone.Notices;

/// <summary>The last notices, newest first. Safe to use from several threads.</summary>
public sealed class NoticeLog(int capacity)
{
    private readonly object _lock = new();
    private readonly LinkedList<Notice> _items = new();

    public void Add(Notice notice)
    {
        lock (_lock)
        {
            _items.AddFirst(notice);
            while (_items.Count > capacity)
            {
                _items.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }

    public IReadOnlyList<Notice> Recent
    {
        get
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }
    }
}
