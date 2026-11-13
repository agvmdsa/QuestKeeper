namespace AqwCompanion.Skua;

public static class SkuaProtocol
{
    public const int Version = 1;
    public const int MaxLineBytes = 16 * 1024;
    public const int MaxTextLength = 300;

    /// <summary>At most this many active quests travel in one status.</summary>
    public const int MaxQuests = 30;

    /// <summary>At most this many factions travel in one status (the game has about 50).</summary>
    public const int MaxFactions = 60;
    /// <summary>The highest count a merged drop line carries.</summary>
    public const int MaxCount = 99999;

    /// <summary>At most this many scripts travel in one result (the 16 KB line cap may leave more out).</summary>
    public const int MaxScripts = 500;

    /// <summary>An option value longer than this is left out, never cut (a cut value would be a different value).</summary>
    public const int MaxOptionValueLength = 200;

    /// <summary>A command line (app to plugin) over this many UTF-8 bytes is dropped.</summary>
    public const int MaxCommandLineBytes = 4 * 1024;

    /// <summary>At most this many plugins are connected at once.</summary>
    public const int MaxInstances = 8;

    /// <summary>The values of <see cref="SkuaStatus.Control"/>; anything else is not reported.</summary>
    public static readonly IReadOnlyList<string> ControlStates = ["idle", "running", "stopping", "settling", "awaitingOptions"];

    /// <summary>The values of <see cref="SkuaOptionDef.Type"/>; anything else travels as <c>other</c>.</summary>
    public static readonly IReadOnlyList<string> OptionTypes = ["bool", "int", "double", "string", "enum", "other"];

    public const string PipeName = "SkuaLink.SkuaBridge.v1";

    /// <summary>The Skua version the plugin was built and tested against.</summary>
    public const string TestedSkuaVersion = "1.4.5.0";
}
