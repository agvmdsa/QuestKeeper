using System.Globalization;

namespace AqwCompanion.Skua;

/// <summary>The ready-made text of each log line. The text never depends on the machine's culture and never carries gold balances.</summary>
public static class SkuaLogFormatter
{
    private const string Drop = "Drop";
    private const string Received = "Received";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static SkuaLogEntry Bought(DateTimeOffset at, string? name, int id, int? quantity) =>
        Entry(at, SkuaLogKind.Bought, $"Bought {Item(name, id)}{(quantity is { } q ? $" x{q.ToString(Inv)}" : string.Empty)}");

    public static SkuaLogEntry Sold(DateTimeOffset at, string? name, int id, int quantity, int cost, bool isAc) =>
        Entry(at, SkuaLogKind.Sold, $"Sold {Item(name, id)} x{quantity.ToString(Inv)} for {cost.ToString("N0", Inv)} {(isAc ? "AC" : "gold")}");

    /// <summary>
    /// A drop. Skua raises it for an item offered in the drop list (<paramref name="addedDirectly"/> false, the usual farming
    /// case; accepting it later raises nothing more) and for an item the game put straight into the inventory, such as a
    /// reward (<paramref name="addedDirectly"/> true).
    /// The entry carries the item name (one line, cut to the text cap) and a count of 1 when the name is known, so SkuaLink
    /// can merge repeated drops of it (<see cref="WithCount"/>).
    /// </summary>
    public static SkuaLogEntry Dropped(DateTimeOffset at, string? name, bool addedDirectly)
    {
        var item = string.IsNullOrWhiteSpace(name) ? null : Single(name);
        return Entry(at, SkuaLogKind.Dropped, DropText(addedDirectly ? Received : Drop, item, 1)) with { Item = item };
    }

    /// <summary>
    /// A drop line standing for <paramref name="count"/> drops (kept within 1 to <see cref="SkuaProtocol.MaxCount"/>), last
    /// updated at <paramref name="at"/>: "Drop: {item} x{count}" or "Received: {item} x{count}", no suffix for 1. The kind,
    /// the item and the Drop/Received prefix stay those of <paramref name="entry"/>.
    /// </summary>
    public static SkuaLogEntry WithCount(SkuaLogEntry entry, int count, DateTimeOffset at)
    {
        count = Math.Clamp(count, 1, SkuaProtocol.MaxCount);
        var prefix = DropKind(entry) ?? Drop;
        return Entry(at, entry.Kind, DropText(prefix, entry.Item, count)) with { Item = entry.Item, Count = count };
    }

    /// <summary>"Drop" or "Received" for a drop line, null for any other line.</summary>
    public static string? DropKind(SkuaLogEntry entry) =>
        entry.Kind != SkuaLogKind.Dropped ? null
        : entry.Text.StartsWith(Received + ": ", StringComparison.Ordinal) ? Received
        : entry.Text.StartsWith(Drop + ": ", StringComparison.Ordinal) ? Drop
        : null;

    private static string DropText(string prefix, string? item, int count) =>
        $"{prefix}: {item ?? "unknown item"}{(count > 1 ? $" x{count.ToString(Inv)}" : string.Empty)}";

    public static SkuaLogEntry MapChanged(DateTimeOffset at, string map) => Entry(at, SkuaLogKind.MapChanged, $"Map: {map}");

    public static SkuaLogEntry Died(DateTimeOffset at) => Entry(at, SkuaLogKind.Died, "Died");

    public static SkuaLogEntry LoggedOut(DateTimeOffset at) => Entry(at, SkuaLogKind.LoggedOut, "Logged out");

    public static SkuaLogEntry Relogin(DateTimeOffset at, bool kicked) =>
        Entry(at, SkuaLogKind.Relogin, kicked ? "Disconnected by the server, logging in again" : "Logging in again");

    public static SkuaLogEntry ScriptStarted(DateTimeOffset at, string script) => Entry(at, SkuaLogKind.ScriptStarted, $"Script started: {script}");

    public static SkuaLogEntry ScriptStopped(DateTimeOffset at, string script) => Entry(at, SkuaLogKind.ScriptStopped, $"Script stopped: {script}");

    public static SkuaLogEntry ScriptError(DateTimeOffset at, string message) =>
        Entry(at, SkuaLogKind.ScriptError, $"Script stopped with an error: {message}");

    private static string Item(string? name, int id) => string.IsNullOrWhiteSpace(name) ? $"item #{id.ToString(Inv)}" : name;

    private static SkuaLogEntry Entry(DateTimeOffset at, SkuaLogKind kind, string text) => new(at, kind, Single(text));

    /// <summary>One line, trimmed and cut to the text cap without splitting a surrogate pair.</summary>
    private static string Single(string text)
    {
        var single = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        var length = single.Length;
        if (length > SkuaProtocol.MaxTextLength)
        {
            length = char.IsHighSurrogate(single[SkuaProtocol.MaxTextLength - 1]) ? SkuaProtocol.MaxTextLength - 1 : SkuaProtocol.MaxTextLength;
        }

        return single[..length];
    }
}
