using AqwCompanion.Core.Persistence;

namespace AqwCompanion.Tests.TestDoubles;

public sealed class InMemoryStoredDataStore : IStoredDataStore
{
    public Dictionary<string, (object Value, DateTimeOffset At)> Items { get; } = [];
    public bool ThrowOnSave { get; set; }
    public bool ThrowOnLoad { get; set; }
    /// <summary>The version each key was saved under; a key put straight into <see cref="Items"/> counts as version 1.</summary>
    public Dictionary<string, int> Versions { get; } = [];
    public bool FailNext { get; set; }
    public int SaveCalls { get; private set; }
    public int ClearCalls { get; private set; }
    public bool ClearResult { get; set; } = true;

    public StoredValue<T>? Load<T>(string key, int version)
    {
        MaybeThrow(ThrowOnLoad);
        return Items.TryGetValue(key, out var item) && item.Value is T value
            && Versions.GetValueOrDefault(key, 1) == version
            ? new StoredValue<T>(value, item.At)
            : null;
    }

    public void Save<T>(string key, int version, T value, DateTimeOffset readAtUtc)
    {
        SaveCalls++;
        MaybeThrow(ThrowOnSave);
        Items[key] = (value!, readAtUtc);
        Versions[key] = version;
    }

    public void Remove(string key)
    {
        MaybeThrow(false);
        Items.Remove(key);
        Versions.Remove(key);
    }

    public bool Clear()
    {
        ClearCalls++;
        MaybeThrow(false);
        Items.Clear();
        Versions.Clear();
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
