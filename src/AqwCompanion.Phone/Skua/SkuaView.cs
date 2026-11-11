using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

public enum SkuaConnection { NotConnected, Connected, VersionMismatch }

/// <summary>What the Skua tab shows. The log is newest first.</summary>
public sealed record SkuaView(SkuaConnection Connection, SkuaHello? Hello, SkuaStatus? Status, IReadOnlyList<SkuaLogEntry> Log)
{
    public static SkuaView Empty { get; } = new(SkuaConnection.NotConnected, null, null, []);
}
