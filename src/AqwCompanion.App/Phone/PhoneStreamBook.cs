namespace AqwCompanion.App.Phone;

/// <summary>
/// The open event streams of one host start: at most <c>maxTotal</c> in all and <c>maxPerDevice</c> per paired device. A device that
/// opens one more ends its own oldest stream and takes over its slot (a phone that reloads must not lock itself out); any other
/// stream over the total is refused. Thread-safe; a stream's token is cancelled under the lock, and its owner disposes it only after
/// <see cref="Close"/>, which takes the same lock.
/// </summary>
internal sealed class PhoneStreamBook(int maxTotal, int maxPerDevice)
{
    internal sealed class Entry(string? deviceId, CancellationTokenSource cts)
    {
        public string? DeviceId { get; } = deviceId;

        public CancellationTokenSource Cts { get; } = cts;

        /// <summary>Set once the slot is given back (closed, or taken over by a newer stream of the same device).</summary>
        public bool Released { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, List<Entry>> _byDevice = new(StringComparer.Ordinal);
    private int _open;

    public int Open
    {
        get { lock (_lock) { return _open; } }
    }

    public int OpenFor(string deviceId)
    {
        lock (_lock)
        {
            return _byDevice.TryGetValue(deviceId, out var list) ? list.Count : 0;
        }
    }

    /// <summary>The new stream's entry, or null when every slot is taken by others.</summary>
    public Entry? TryOpen(string? deviceId, CancellationTokenSource cts)
    {
        lock (_lock)
        {
            var entry = new Entry(deviceId, cts);
            if (deviceId is not null && _byDevice.TryGetValue(deviceId, out var list) && list.Count >= maxPerDevice)
            {
                // The slot passes from the oldest stream of this device to the new one.
                var oldest = list[0];
                list.RemoveAt(0);
                oldest.Released = true;
                TryCancel(oldest.Cts);
                list.Add(entry);
                return entry;
            }

            if (_open >= maxTotal)
            {
                return null;
            }

            _open++;
            if (deviceId is not null)
            {
                if (!_byDevice.TryGetValue(deviceId, out var own))
                {
                    _byDevice[deviceId] = own = [];
                }

                own.Add(entry);
            }

            return entry;
        }
    }

    public void Close(Entry entry)
    {
        lock (_lock)
        {
            if (entry.Released)
            {
                return;
            }

            entry.Released = true;
            _open--;
            if (entry.DeviceId is { } id && _byDevice.TryGetValue(id, out var list))
            {
                list.Remove(entry);
                if (list.Count == 0)
                {
                    _byDevice.Remove(id);
                }
            }
        }
    }

    /// <summary>Ends every stream of these devices (they were revoked); the streams give their slots back when they close.</summary>
    public void CancelDevices(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            foreach (var id in ids)
            {
                if (_byDevice.TryGetValue(id, out var list))
                {
                    foreach (var entry in list.ToArray())
                    {
                        TryCancel(entry.Cts);
                    }
                }
            }
        }
    }

    private static void TryCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Its stream has already ended.
        }
    }
}
