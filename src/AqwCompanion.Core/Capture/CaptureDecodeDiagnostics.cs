namespace AqwCompanion.Core.Capture;

/// <summary>
/// A snapshot of how the decoding went, as counts only: why messages could not be read, how many messages of each KNOWN protocol
/// command were read, and what became of the player's own profile. A command outside <see cref="KnownCommands"/> is counted
/// under <see cref="OtherCommand"/>; its name is kept in <see cref="OtherNames"/> only when it is made of letters, digits and
/// <c>_ + . -</c>, is at most <see cref="MaxNameLength"/> characters long, and fewer than <see cref="MaxOtherNames"/> distinct
/// names were kept already. Any other name is never kept.
/// </summary>
public sealed record CaptureDecodeDiagnostics(
    IReadOnlyDictionary<DecodeFailure, int> Failures,
    IReadOnlyDictionary<string, int> Commands,
    int InitUserDataAccepted,
    int InitUserDataIgnored,
    int InitUserDataIgnoredNoGold,
    int InitUserDataIgnoredNoName,
    IReadOnlyList<KeyValuePair<string, int>> OtherNames)
{
    /// <summary>The key under which every command outside <see cref="KnownCommands"/> is counted.</summary>
    public const string OtherCommand = "other";

    /// <summary>The most distinct unknown command names kept.</summary>
    public const int MaxOtherNames = 40;

    /// <summary>The longest unknown command name that may be kept.</summary>
    public const int MaxNameLength = 40;

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
        0,
        []);

    /// <summary>
    /// True when the name may be listed: 1 to <see cref="MaxNameLength"/> characters out of A-Z, a-z, 0-9, <c>_</c>, <c>+</c>,
    /// <c>.</c> and <c>-</c>, and not one of the <see cref="KnownCommands"/>.
    /// </summary>
    public static bool IsListableName(string? name)
    {
        if (name is null || name.Length is 0 or > MaxNameLength || KnownCommands.Contains(name) || name == OtherCommand)
        {
            return false;
        }

        foreach (var c in name)
        {
            var ok = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '+' or '.' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>How many messages failed for this reason.</summary>
    public int Failure(DecodeFailure reason) => Failures.TryGetValue(reason, out var count) ? count : 0;

    /// <summary>How many messages carried this command: a known name, or <see cref="OtherCommand"/>.</summary>
    public int Command(string name) => Commands.TryGetValue(name, out var count) ? count : 0;
}
