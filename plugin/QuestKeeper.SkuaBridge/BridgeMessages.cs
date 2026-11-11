using System.Reflection;
using AqwCompanion.Skua;

namespace QuestKeeper.SkuaBridge;

/// <summary>
/// The pure part of the plugin: turns plain values read from Skua into protocol values. Nothing here touches Skua,
/// so every rule about what is sent can be read (and reasoned about) without Skua running.
/// </summary>
internal static class BridgeMessages
{
    /// <summary>The status line. Character, server and map are only reported while logged in (Skua keeps the last values after a logout).</summary>
    public static SkuaStatus Status(
        bool loggedIn, string? username, string? server, string? map, string? loadedScriptPath, bool running, DateTimeOffset? runningSince) =>
        new(
            loggedIn,
            loggedIn ? Blank(username) : null,
            loggedIn ? Blank(server) : null,
            loggedIn ? Blank(map) : null,
            ScriptName(loadedScriptPath),
            running,
            running ? runningSince : null);

    /// <summary>
    /// How many were bought: the quantity now minus the quantity in the snapshot taken before, when positive; otherwise
    /// unknown (null), for example when the game had not yet applied the purchase or the snapshot was taken after it.
    /// </summary>
    public static int? BoughtQuantity(int before, int? now) => now is { } n && n - before > 0 ? n - before : null;

    /// <summary>The script's file name without folder or extension, so no local path (which holds the Windows user name) leaves the plugin.</summary>
    public static string? ScriptName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]);
        return Blank(name);
    }

    /// <summary>The message of the error that stopped a script. Skua invokes ScriptMain by reflection, so the real error is usually the inner one.</summary>
    public static string ErrorText(Exception exception)
    {
        var actual = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        return Blank(actual.Message) ?? actual.GetType().Name;
    }

    /// <summary>The plugin's own version as the release names it (MinVer's informational version without build metadata).</summary>
    public static string PluginVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (Blank(informational) is { } text)
        {
            var plus = text.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? text[..plus] : text;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
