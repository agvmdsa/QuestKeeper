namespace AqwCompanion.Skua;

/// <summary>
/// QuestAccepted and QuestTurnedIn are no longer sent (the status carries the active quests instead); they stay so
/// logs kept by an older version still load.
/// </summary>
public enum SkuaLogKind
{
    Bought, Sold, Dropped, MapChanged, QuestAccepted, QuestTurnedIn, Died, LoggedOut, Relogin, ScriptStarted, ScriptStopped, ScriptError,
}

public sealed record SkuaHello(string PluginVersion, string SkuaVersion);

/// <summary>An active quest. <paramref name="Ready"/> is Skua's own "can be turned in" flag.</summary>
public sealed record SkuaQuest(int Id, string Name, bool Ready);

/// <summary>
/// A faction standing as Skua reads it (<c>world.myAvatar.factions</c>). <paramref name="Rep"/> is the reputation earned
/// inside the current rank (the game's iSpillRep), <paramref name="RequiredRep"/> the size of the current rank step
/// (iRepToRank, 0 at the maximum rank), <paramref name="TotalRep"/> the faction's total (iRep).
/// </summary>
public sealed record SkuaFaction(int Id, string Name, int Rank, int TotalRep, int Rep, int RequiredRep);

/// <summary>
/// What Skua is doing. <paramref name="Quests"/> null means "this plugin does not report quests" (an older plugin);
/// an empty list means "no active quests". <paramref name="Factions"/> follows the same rule for the reputation. Two
/// statuses are equal when their quest lists and their faction lists hold the same entries in the same order.
/// </summary>
public sealed record SkuaStatus(
    bool LoggedIn, string? Character, string? Server, string? Map, string? Script, bool ScriptRunning, DateTimeOffset? RunningSince,
    IReadOnlyList<SkuaQuest>? Quests = null, IReadOnlyList<SkuaFaction>? Factions = null)
{
    public bool Equals(SkuaStatus? other) =>
        other is not null
        && LoggedIn == other.LoggedIn
        && Character == other.Character
        && Server == other.Server
        && Map == other.Map
        && Script == other.Script
        && ScriptRunning == other.ScriptRunning
        && RunningSince == other.RunningSince
        && SameList(Quests, other.Quests)
        && SameList(Factions, other.Factions);

    private static bool SameList<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LoggedIn);
        hash.Add(Character);
        hash.Add(Server);
        hash.Add(Map);
        hash.Add(Script);
        hash.Add(ScriptRunning);
        hash.Add(RunningSince);
        hash.Add(Quests?.Count ?? -1);
        foreach (var quest in Quests ?? [])
        {
            hash.Add(quest);
        }

        hash.Add(Factions?.Count ?? -1);
        foreach (var faction in Factions ?? [])
        {
            hash.Add(faction);
        }

        return hash.ToHashCode();
    }
}

public sealed record SkuaLogEntry(DateTimeOffset At, SkuaLogKind Kind, string Text);

public abstract record SkuaMessage;

public sealed record HelloMessage(SkuaHello Hello) : SkuaMessage;

public sealed record StatusMessage(SkuaStatus Status) : SkuaMessage;

public sealed record LogMessage(SkuaLogEntry Entry) : SkuaMessage;

/// <summary>A well-formed line of another protocol version; the receiver tells the user to update.</summary>
public sealed record UnsupportedVersionMessage(int Version) : SkuaMessage;
