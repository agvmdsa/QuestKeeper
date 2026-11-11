using System.Globalization;

namespace AqwCompanion.Skua;

/// <summary>The ready-made text of each log line. The text never depends on the machine's culture and never carries gold balances.</summary>
public static class SkuaLogFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static SkuaLogEntry Bought(DateTimeOffset at, string? name, int id, int? quantity) =>
        Entry(at, SkuaLogKind.Bought, $"Bought {Item(name, id)}{(quantity is { } q ? $" x{q.ToString(Inv)}" : string.Empty)}");

    public static SkuaLogEntry Sold(DateTimeOffset at, string? name, int id, int quantity, int cost, bool isAc) =>
        Entry(at, SkuaLogKind.Sold, $"Sold {Item(name, id)} x{quantity.ToString(Inv)} for {cost.ToString("N0", Inv)} {(isAc ? "AC" : "gold")}");

    /// <summary>
    /// A drop. Skua raises it for an item offered in the drop list (<paramref name="addedDirectly"/> false, the usual farming
    /// case; accepting it later raises nothing more) and for an item the game put straight into the inventory, such as a
    /// reward (<paramref name="addedDirectly"/> true).
    /// </summary>
    public static SkuaLogEntry Dropped(DateTimeOffset at, string? name, bool addedDirectly) =>
        Entry(at, SkuaLogKind.Dropped, $"{(addedDirectly ? "Received" : "Drop")}: {(string.IsNullOrWhiteSpace(name) ? "unknown item" : name)}");

    public static SkuaLogEntry MapChanged(DateTimeOffset at, string map) => Entry(at, SkuaLogKind.MapChanged, $"Map: {map}");

    public static SkuaLogEntry QuestAccepted(DateTimeOffset at, int id, string? name) =>
        Entry(at, SkuaLogKind.QuestAccepted, $"Quest accepted: {Quest(name, id)}");

    public static SkuaLogEntry QuestTurnedIn(DateTimeOffset at, int id, string? name) =>
        Entry(at, SkuaLogKind.QuestTurnedIn, $"Quest turned in: {Quest(name, id)}");

    public static SkuaLogEntry Died(DateTimeOffset at) => Entry(at, SkuaLogKind.Died, "Died");

    public static SkuaLogEntry LoggedOut(DateTimeOffset at) => Entry(at, SkuaLogKind.LoggedOut, "Logged out");

    public static SkuaLogEntry Relogin(DateTimeOffset at, bool kicked) =>
        Entry(at, SkuaLogKind.Relogin, kicked ? "Disconnected by the server, logging in again" : "Logging in again");

    public static SkuaLogEntry ScriptStarted(DateTimeOffset at, string script) => Entry(at, SkuaLogKind.ScriptStarted, $"Script started: {script}");

    public static SkuaLogEntry ScriptStopped(DateTimeOffset at, string script) => Entry(at, SkuaLogKind.ScriptStopped, $"Script stopped: {script}");

    public static SkuaLogEntry ScriptError(DateTimeOffset at, string message) =>
        Entry(at, SkuaLogKind.ScriptError, $"Script stopped with an error: {message}");

    private static string Item(string? name, int id) => string.IsNullOrWhiteSpace(name) ? $"item #{id.ToString(Inv)}" : name;

    private static string Quest(string? name, int id) => string.IsNullOrWhiteSpace(name) ? $"#{id.ToString(Inv)}" : name;

    private static SkuaLogEntry Entry(DateTimeOffset at, SkuaLogKind kind, string text)
    {
        var single = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        var length = single.Length;
        if (length > SkuaProtocol.MaxTextLength)
        {
            length = char.IsHighSurrogate(single[SkuaProtocol.MaxTextLength - 1]) ? SkuaProtocol.MaxTextLength - 1 : SkuaProtocol.MaxTextLength;
        }

        return new SkuaLogEntry(at, kind, single[..length]);
    }
}
