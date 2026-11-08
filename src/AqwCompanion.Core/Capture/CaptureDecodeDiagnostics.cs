namespace AqwCompanion.Core.Capture;

/// <summary>
/// A snapshot of how the decoding went, as counts only: why messages could not be read, how many messages of each KNOWN protocol
/// command were read, and what became of the player's own profile. A command outside <see cref="KnownCommands"/> is counted
/// under <see cref="OtherCommand"/> and its name is never kept.
/// </summary>
public sealed record CaptureDecodeDiagnostics(
    IReadOnlyDictionary<DecodeFailure, int> Failures,
    IReadOnlyDictionary<string, int> Commands,
    int InitUserDataAccepted,
    int InitUserDataIgnored,
    int InitUserDataIgnoredNoGold,
    int InitUserDataIgnoredNoName)
{
    /// <summary>The key under which every command outside <see cref="KnownCommands"/> is counted.</summary>
    public const string OtherCommand = "other";

    /// <summary>The only command names the diagnostics may keep or show.</summary>
    public static readonly IReadOnlyList<string> KnownCommands =
    [
        "initUserData", "initUserDatas", "loadInventoryBig", "loadShop", "moveToArea", "sAct", "stu", "ct", "cb", "mtls",
        "uotls", "respawnMon", "equipItem", "wearItem", "unequipItem", "dropItem", "addItems", "getDrop", "addGoldExp",
        "updateClass", "loadPrefs", "loadWarVars", "mailList", "aura+p", "clearAuras", "auSnap", "sar", "sars", "bankToInv",
    ];

    /// <summary>Everything zero.</summary>
    public static CaptureDecodeDiagnostics Empty { get; } = new(
        Enum.GetValues<DecodeFailure>().ToDictionary(f => f, _ => 0),
        KnownCommands.Append(OtherCommand).ToDictionary(c => c, _ => 0),
        0,
        0,
        0,
        0);

    /// <summary>How many messages failed for this reason.</summary>
    public int Failure(DecodeFailure reason) => Failures.TryGetValue(reason, out var count) ? count : 0;

    /// <summary>How many messages carried this command: a known name, or <see cref="OtherCommand"/>.</summary>
    public int Command(string name) => Commands.TryGetValue(name, out var count) ? count : 0;
}
