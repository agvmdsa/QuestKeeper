using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>Keeps the log between runs. Implementations never throw.</summary>
public interface ISkuaLogStore
{
    /// <summary>The stored log; empty when there is none or it cannot be read.</summary>
    IReadOnlyList<SkuaLogEntry> Load();

    void Save(IReadOnlyList<SkuaLogEntry> log);

    void Delete();

    /// <summary>
    /// Like <see cref="Load"/>, but false when the stored log exists and cannot be read (locked, broken), so a caller can
    /// tell that from an empty or missing log. A store that cannot tell answers what <see cref="Load"/> gives, as read.
    /// </summary>
    bool TryLoad(out IReadOnlyList<SkuaLogEntry> log)
    {
        log = Load();
        return true;
    }

    /// <summary>Like <see cref="Save"/>, but false when the log did not reach the store.</summary>
    bool TrySave(IReadOnlyList<SkuaLogEntry> log)
    {
        Save(log);
        return true;
    }
}

/// <summary>The Skua logs, one per character (<see cref="SkuaLogFiles.FileName"/>), plus the single log of older versions. Never throw.</summary>
public interface ISkuaLogStores
{
    ISkuaLogStore ForCharacter(string character);

    /// <summary>The log of a version before several Skua (<c>skua-log.json</c>).</summary>
    ISkuaLogStore Legacy { get; }

    /// <summary>Deletes every Skua log, the legacy one included.</summary>
    void DeleteAll();
}
