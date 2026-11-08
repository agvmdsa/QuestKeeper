namespace AqwCompanion.Core.Persistence;

/// <summary>A stored value together with the moment it was read from the site.</summary>
public sealed record StoredValue<T>(T Value, DateTimeOffset ReadAtUtc);

/// <summary>Keeps parsed values on disk, one per key. Implementations must not store anything but what they are given.</summary>
public interface IStoredDataStore
{
    /// <summary>Returns the stored value, or null when absent, stored under another version than the one asked for, or unreadable.</summary>
    StoredValue<T>? Load<T>(string key, int version);

    /// <summary>Stores the value under the key with the version of its shape, replacing any previous one.</summary>
    void Save<T>(string key, int version, T value, DateTimeOffset readAtUtc);

    /// <summary>Deletes the value stored under the key, if any.</summary>
    void Remove(string key);

    /// <summary>Deletes the database file(s) and everything in them; false when something that exists could not be deleted.</summary>
    bool Clear();
}
