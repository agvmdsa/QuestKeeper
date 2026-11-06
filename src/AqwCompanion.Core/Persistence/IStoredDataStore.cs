namespace AqwCompanion.Core.Persistence;

/// <summary>A stored value together with the moment it was read from the site.</summary>
public sealed record StoredValue<T>(T Value, DateTimeOffset ReadAtUtc);

/// <summary>Keeps parsed values on disk, one per key. Implementations must not store anything but what they are given.</summary>
public interface IStoredDataStore
{
    /// <summary>Returns the stored value, or null when absent, of an unknown schema version or unreadable.</summary>
    StoredValue<T>? Load<T>(string key);

    /// <summary>Stores the value under the key, replacing any previous one.</summary>
    void Save<T>(string key, T value, DateTimeOffset readAtUtc);

    /// <summary>Deletes the value stored under the key, if any.</summary>
    void Remove(string key);

    /// <summary>Deletes the database file(s) and everything in them; false when something that exists could not be deleted.</summary>
    bool Clear();
}
