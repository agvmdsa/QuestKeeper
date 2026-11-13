using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>Keeps the log between runs. Implementations never throw.</summary>
public interface ISkuaLogStore
{
    IReadOnlyList<SkuaLogEntry> Load();

    void Save(IReadOnlyList<SkuaLogEntry> log);

    void Delete();
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
