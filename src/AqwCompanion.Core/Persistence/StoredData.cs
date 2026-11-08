namespace AqwCompanion.Core.Persistence;

/// <summary>
/// Typed, failure-proof access to the stored data. Never throws (except for <see cref="OutOfMemoryException"/>): a failure
/// is reported to the callback by operation and exception type name only, never by message. Keys that are no longer used
/// are removed after the first successful save.
/// </summary>
public sealed class StoredData(IStoredDataStore store, IReadOnlyList<string> obsoleteKeys, Action<string, string>? onFailure = null)
{
    private bool _obsoleteRemoved;

    /// <summary>The stored value, or null on any failure (absent, unreadable, another version, database error).</summary>
    public StoredValue<T>? Load<T>(StoredKey<T> key) =>
        Guard("Load", () => store.Load<T>(key.Name, key.Version), default);

    /// <summary>Stores the value; false on failure. The first success also removes the obsolete keys, once and best effort.</summary>
    public bool Save<T>(StoredKey<T> key, T value, DateTimeOffset readAtUtc)
    {
        if (!Guard("Save", () => { store.Save(key.Name, key.Version, value, readAtUtc); return true; }, false))
        {
            return false;
        }

        RemoveObsoleteOnce();
        return true;
    }

    /// <summary>Deletes the value of the key; false on failure.</summary>
    public bool Remove<T>(StoredKey<T> key) =>
        Guard("Remove", () => { store.Remove(key.Name); return true; }, false);

    /// <summary>Deletes everything stored; false on failure or when something could not be deleted.</summary>
    public bool Clear() => Guard("Clear", store.Clear, false);

    private void RemoveObsoleteOnce()
    {
        if (_obsoleteRemoved)
        {
            return;
        }

        _obsoleteRemoved = true;
        foreach (var name in obsoleteKeys)
        {
            Guard("RemoveObsolete", () => { store.Remove(name); return true; }, false);
        }
    }

    private TResult Guard<TResult>(string operation, Func<TResult> action, TResult fallback)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try
            {
                onFailure?.Invoke(operation, ex.GetType().Name);
            }
            catch (Exception callbackEx) when (callbackEx is not OutOfMemoryException)
            {
                // A failing report must not turn a handled failure into a thrown one.
            }

            return fallback;
        }
    }
}
