using AqwCompanion.Skua;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace SkuaLink.SkuaBridge;

/// <summary>
/// The thin Skua-bound glue behind the commands: Skua's script manager as an <see cref="ISkuaScriptHost"/>, and the script
/// catalog over Skua's scripts folder (<c>ClientFileSources.SkuaScriptsDIR</c>, <c>%AppData%\Skua\Scripts</c>). Every
/// decision (which id, whether a start is allowed, the answers) is in <see cref="SkuaCommandHandler"/>, which is unit-tested
/// without Skua. The members used are Skua's plain methods (no Skua.Core, no UI): <c>ScriptRunning</c>,
/// <c>SetLoadedScript</c>, <c>StartScript</c>, <c>Config</c> and <c>StopScript</c>.
/// </summary>
internal sealed class ScriptControl : ISkuaScriptHost
{
    private readonly IScriptStatus _manager;

    public ScriptControl(IScriptStatus manager) => _manager = manager;

    public bool ScriptRunning => _manager.ScriptRunning;

    public bool ScriptExists(string fullPath) => File.Exists(fullPath);

    public void SetLoadedScript(string fullPath) => _manager.SetLoadedScript(fullPath);

    /// <summary>Skua compiles on a pool task and runs the script on its own "Script Thread"; this returns once the thread started.</summary>
    public Task<Exception?> StartScript() => _manager.StartScript();

    /// <summary>Skua loads the script's options (<c>Config</c>) while starting it, so this is read after the start.</summary>
    public bool LoadedScriptHasOptions() =>
        _manager.Config is { } config && (config.Options?.Count > 0 || config.MultipleOptions?.Count > 0);

    /// <summary>Called on a pool thread by the handler (Skua's stop refuses to wait when called on the script thread).</summary>
    public Task StopScript() => _manager.StopScript().AsTask();

    /// <summary>
    /// The catalog of Skua's scripts folder. A missing folder lists nothing. Links are skipped while listing and refused
    /// again when an id is resolved (the catalog's own check, through <see cref="IsLink"/>).
    /// </summary>
    public static SkuaScriptCatalog Catalog() => new(ClientFileSources.SkuaScriptsDIR, ListFiles, IsLink);

    private static IEnumerable<string> ListFiles(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.cs", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchType = MatchType.Simple,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            })
            : [];

    /// <summary>True for a reparse point (symbolic link, junction); the catalog treats a failed check as a link.</summary>
    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
