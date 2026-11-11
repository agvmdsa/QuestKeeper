using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>Keeps the log between runs. Implementations never throw.</summary>
public interface ISkuaLogStore
{
    IReadOnlyList<SkuaLogEntry> Load();

    void Save(IReadOnlyList<SkuaLogEntry> log);

    void Delete();
}
