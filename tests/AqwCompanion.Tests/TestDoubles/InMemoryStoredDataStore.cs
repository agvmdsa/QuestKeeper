using AqwCompanion.Core.Persistence;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class InMemoryStoredDataStore : IStoredDataStore
{
    public Dictionary<string, (object Value, DateTimeOffset At)> Items { get; } = [];
    public bool ThrowOnSave { get; set; }
    public bool ThrowOnLoad { get; set; }
    public bool FailNext { get; set; }
    public int SaveCalls { get; private set; }
    public int ClearCalls { get; private set; }
    public bool ClearResult { get; set; } = true;

    public StoredValue<T>? Load<T>(string key)
    {
        MaybeThrow(ThrowOnLoad);
        return Items.TryGetValue(key, out var item) && item.Value is T value
            ? new StoredValue<T>(value, item.At)
            : null;
    }

    public void Save<T>(string key, T value, DateTimeOffset readAtUtc)
    {
        SaveCalls++;
        MaybeThrow(ThrowOnSave);
        Items[key] = (value!, readAtUtc);
    }

    public void Remove(string key)
    {
        MaybeThrow(false);
        Items.Remove(key);
    }

    public bool Clear()
    {
        ClearCalls++;
        MaybeThrow(false);
        Items.Clear();
        return ClearResult;
    }

    private void MaybeThrow(bool always)
    {
        if (always || FailNext)
        {
            FailNext = false;
            throw new IOException("simulated store failure");
        }
    }
}
