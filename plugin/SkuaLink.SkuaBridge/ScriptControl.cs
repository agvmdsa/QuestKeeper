using System.Collections.Concurrent;
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
    public static SkuaScriptCatalog Catalog() => new(ClientFileSources.SkuaScriptsDIR, ListFiles, IsLink, null, ReadFacts,
        new SkuaScriptFactsOptions { Cached = CachedFacts, Retain = Retain });

    private const int MaxSourceBytes = 2 * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, SkuaScriptFacts Facts)> FactsByPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The facts of an unchanged file from an earlier read, or null; only looks at the file's size and time.</summary>
    private static SkuaScriptFacts? CachedFacts(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return FactsByPath.TryGetValue(path, out var known) && known.Length == info.Length && known.Written == info.LastWriteTimeUtc
                ? known.Facts
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Forgets the facts of files the last scan did not see (deleted or moved), so the cache cannot grow without end.</summary>
    private static void Retain(IReadOnlySet<string> seen)
    {
        foreach (var path in FactsByPath.Keys)
        {
            if (!seen.Contains(path))
            {
                FactsByPath.TryRemove(path, out _);
            }
        }
    }

    /// <summary>
    /// What one read of the file tells: whether it declares <c>void ScriptMain(</c> (a bot, not a library) and its header
    /// comment (name, description, tags). Reads at most 2 MB once, decoded by byte order mark (see <see cref="SkuaScriptFileText"/>); the result is cached per path by (length, last
    /// write time), so a rescan does not reread unchanged files. Any error means "list it" without header data (null).
    /// Runs on the command worker, never on the pipe reader.
    /// </summary>
    private static SkuaScriptFacts? ReadFacts(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var length = info.Length;
            var written = info.LastWriteTimeUtc;
            if (FactsByPath.TryGetValue(path, out var known) && known.Length == length && known.Written == written)
            {
                return known.Facts;
            }

            var buffer = new byte[Math.Min(length, MaxSourceBytes)];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            var facts = SkuaScriptFileText.Facts(buffer, read, length > MaxSourceBytes);
            FactsByPath[path] = (length, written, facts);
            return facts;
        }
        catch (Exception)
        {
            return null;
        }
    }

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
