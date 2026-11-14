using System.Security.Cryptography;
using System.Text;

namespace AqwCompanion.Skua;

/// <summary>
/// Skua's scripts folder as a list of opaque ids. The phone only ever sends back an id from the last <see cref="Scan"/>;
/// the path it stands for is rebuilt here and checked again before use. Pure: the folder listing and the link check are
/// injected, so the same code runs in the plugin and in the tests.
/// </summary>
public sealed class SkuaScriptCatalog
{
    /// <summary>Skua's compile cache inside the scripts folder; never listed.</summary>
    public const string CacheFolder = "Cached-Scripts";

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly Func<string, IEnumerable<string>> _listFiles;
    private readonly Func<string, bool> _isLink;
    private readonly Func<string, bool>? _isBotFile;
    private readonly Func<string, SkuaScriptFacts?>? _facts;
    private readonly object _gate = new();
    private Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    /// <param name="scriptsRoot">The scripts folder (absolute).</param>
    /// <param name="listFiles">Every file under a folder, recursively, as full paths.</param>
    /// <param name="isLink">True when a file or folder is a reparse point or symbolic link.</param>
    /// <param name="isBotFile">Optional: the full path in, true = list it (a bot, not a library). Asked after the name rule;
    /// null means no check, and an exception counts as "list it".</param>
    /// <param name="facts">Optional: the full path in, what one read of the file told (bot or not, header title,
    /// description and tags). Asked once per listed file after the name rule; <c>IsBot = false</c> excludes it, null or an
    /// exception lists it without header data.</param>
    public SkuaScriptCatalog(string scriptsRoot, Func<string, IEnumerable<string>> listFiles, Func<string, bool> isLink,
        Func<string, bool>? isBotFile = null, Func<string, SkuaScriptFacts?>? facts = null)
    {
        _isBotFile = isBotFile;
        _facts = facts;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scriptsRoot));
        _rootPrefix = _root + Path.DirectorySeparatorChar;
        _listFiles = listFiles;
        _isLink = isLink;
    }

    /// <summary>True when the last <see cref="Scan"/> left scripts out because of <see cref="SkuaProtocol.MaxScripts"/>.</summary>
    public bool Truncated { get; private set; }

    /// <summary>
    /// Lists every <c>.cs</c> file under the root except Skua's cache and library files (see <see cref="IsLibraryName"/>,
    /// then the optional bot-file check), sorted by folder then name, at most
    /// <see cref="SkuaProtocol.MaxScripts"/>. Only the ids of this scan resolve afterwards.
    /// </summary>
    public IReadOnlyList<SkuaScriptInfo> Scan()
    {
        var found = new Dictionary<string, (SkuaScriptInfo Info, string FullPath)>(StringComparer.Ordinal);
        foreach (var file in _listFiles(_root))
        {
            if (Relative(file) is not { } relative || found.ContainsKey(relative))
            {
                continue;
            }

            if (IsLibraryName(relative))
            {
                continue;
            }

            var slash = relative.LastIndexOf('/');
            var folder = slash < 0 ? "" : relative[..slash];
            var name = Path.GetFileNameWithoutExtension(relative[(slash + 1)..]);
            if (!IsBotFile(file))
            {
                continue;
            }

            var facts = ReadFacts(file);
            if (facts is { IsBot: false })
            {
                continue;
            }

            found[relative] = (new SkuaScriptInfo(IdOf(relative), name, folder, facts?.Title, facts?.Description, facts?.Tags), _rootPrefix + relative.Replace('/', Path.DirectorySeparatorChar));
        }

        var sorted = found.Values
            .OrderBy(s => s.Info.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Info.Folder, StringComparer.Ordinal)
            .ThenBy(s => s.Info.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Info.Name, StringComparer.Ordinal)
            .ToList();
        var kept = sorted.Take(SkuaProtocol.MaxScripts).ToList();

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var script in kept)
        {
            paths.TryAdd(script.Info.Id, script.FullPath);
        }

        lock (_gate)
        {
            _paths = paths;
            Truncated = sorted.Count > kept.Count;
        }

        return kept.Select(s => s.Info).ToList();
    }

    /// <summary>
    /// True for a library by name: a folder segment equal to <c>Core</c>, or a file name starting with <c>Core</c>
    /// (case-insensitive). Only a prefix counts, so <c>ScoreTracker.cs</c> or <c>Encore.cs</c> stay listed.
    /// </summary>
    public static bool IsLibraryName(string relativePath)
    {
        var parts = relativePath.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Core", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return parts[^1].StartsWith("Core", StringComparison.OrdinalIgnoreCase);
    }

    private SkuaScriptFacts? ReadFacts(string fullPath)
    {
        if (_facts is null)
        {
            return null;
        }

        try
        {
            return _facts(fullPath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool IsBotFile(string fullPath)
    {
        if (_isBotFile is null)
        {
            return true;
        }

        try
        {
            return _isBotFile(fullPath);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// The full path of a script id from the last <see cref="Scan"/>, checked again: inside the root, a <c>.cs</c> file,
    /// not in Skua's cache, and neither the file nor any folder between it and the root a link. Never throws.
    /// </summary>
    public bool TryResolve(string id, out string fullPath)
    {
        fullPath = "";
        string? path;
        lock (_gate)
        {
            if (id is null || !_paths.TryGetValue(id, out path))
            {
                return false;
            }
        }

        try
        {
            var full = Path.GetFullPath(path);
            if (Relative(full) is null || IsLinked(full))
            {
                return false;
            }

            fullPath = full;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The id of a path relative to the root with <c>/</c> separators: the first 8 bytes of its SHA-256, lowercase hex.</summary>
    public static string IdOf(string relativePath) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath)).AsSpan(0, 8));

    /// <summary>The path relative to the root with <c>/</c> separators, or null when the file is not a listable script.</summary>
    private string? Relative(string file)
    {
        string full;
        try
        {
            if (string.IsNullOrEmpty(file) || !Path.IsPathFullyQualified(file))
            {
                return null;
            }

            full = Path.GetFullPath(file);
        }
        catch (Exception)
        {
            return null;
        }

        if (!full.StartsWith(_rootPrefix, PathComparison)
            || !full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(full).Length == 0)
        {
            return null;
        }

        var relative = full[_rootPrefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
        var firstSlash = relative.IndexOf('/');
        if (firstSlash >= 0 && relative[..firstSlash].Equals(CacheFolder, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return relative;
    }

    /// <summary>True when the file or any folder between it and the root is a link; a failed check counts as a link.</summary>
    private bool IsLinked(string full)
    {
        for (var current = full; current.Length > _rootPrefix.Length; current = Path.GetDirectoryName(current) ?? "")
        {
            if (_isLink(current))
            {
                return true;
            }
        }

        return false;
    }
}
