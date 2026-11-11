namespace AqwCompanion.Skua;

public enum SkuaLogKind
{
    Bought, Sold, Dropped, MapChanged, QuestAccepted, QuestTurnedIn, Died, LoggedOut, Relogin, ScriptStarted, ScriptStopped, ScriptError,
}

public sealed record SkuaHello(string PluginVersion, string SkuaVersion);

public sealed record SkuaStatus(bool LoggedIn, string? Character, string? Server, string? Map, string? Script, bool ScriptRunning, DateTimeOffset? RunningSince);

public sealed record SkuaLogEntry(DateTimeOffset At, SkuaLogKind Kind, string Text);

public abstract record SkuaMessage;

public sealed record HelloMessage(SkuaHello Hello) : SkuaMessage;

public sealed record StatusMessage(SkuaStatus Status) : SkuaMessage;

public sealed record LogMessage(SkuaLogEntry Entry) : SkuaMessage;

/// <summary>A well-formed line of another protocol version; the receiver tells the user to update.</summary>
public sealed record UnsupportedVersionMessage(int Version) : SkuaMessage;
