using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

/// <summary>One JSON file SkuaLink keeps about Skua scripts, read and written whole. Implementations never throw.</summary>
public interface ISkuaScriptFile
{
    /// <summary>The file's text, or null when there is none or it cannot be read.</summary>
    string? Read();

    void Write(string text);

    void Delete();
}

/// <summary>What a book keeps about one script, and when it was last used.</summary>
public sealed record SkuaScriptEntry<T>(T Value, DateTimeOffset LastUsed);

/// <summary>The file a book is saved to.</summary>
public sealed record SkuaScriptFileModel<T>(int Version, Dictionary<string, SkuaScriptEntry<T>> Scripts);

/// <summary>
/// Something SkuaLink keeps per Skua script id (the 16 hex characters the plugin gives a script), saved to one file. An entry
/// not used for <see cref="Expiry"/> is dropped (when the file is loaded and when it is saved). Loaded on first use; a file
/// that cannot be read is no entries. <see cref="IForgettable.Forget"/> clears the entries and deletes the file. Safe from any
/// thread.
/// </summary>
public abstract partial class SkuaScriptBook<T> : IForgettable
{
    /// <summary>An entry not used for this long is dropped.</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(90);

    /// <summary>At most this many scripts are kept (the least recently used go first).</summary>
    public const int MaxEntries = SkuaProtocol.MaxScripts;

    // Using an entry writes the file again only when its last use is older than this, so listing the scripts is not a write each time.
    private static readonly TimeSpan TouchGranularity = TimeSpan.FromHours(1);

    private readonly object _lock = new();
    private readonly ISkuaScriptFile _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly JsonTypeInfo<SkuaScriptFileModel<T>> _typeInfo;
    private Dictionary<string, SkuaScriptEntry<T>>? _entries;
    private bool _quiesced;

    protected SkuaScriptBook(ISkuaScriptFile file, Func<DateTimeOffset> now, JsonTypeInfo<SkuaScriptFileModel<T>> typeInfo)
    {
        _file = file;
        _now = now;
        _typeInfo = typeInfo;
    }

    public abstract string Name { get; }

    /// <summary>True for a script id as the plugin makes them: 16 lowercase hex characters.</summary>
    public static bool IsScriptId(string? id) => id is not null && ScriptIdPattern().IsMatch(id);

    public void Quiesce()
    {
        lock (_lock)
        {
            _quiesced = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _quiesced = false;
        }
    }

    public void Forget()
    {
        lock (_lock)
        {
            _entries = [];
            _file.Delete();
            _quiesced = false;
        }
    }

    /// <summary>Counts a use of each of these scripts that has an entry (an entry in use does not expire).</summary>
    public void Touch(IEnumerable<string> ids) => Find(ids);

    /// <summary>The value kept for the script, or default; finding it counts as a use.</summary>
    protected bool TryGet(string id, out T value)
    {
        lock (_lock)
        {
            var entries = Entries();
            if (entries.TryGetValue(id, out var entry))
            {
                value = entry.Value;
                if (Touch(entries, id))
                {
                    Save(entries);
                }

                return true;
            }

            value = default!;
            return false;
        }
    }

    /// <summary>The values kept for these scripts (a script with none is left out); each one found counts as a use.</summary>
    protected Dictionary<string, T> Find(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            var entries = Entries();
            var found = new Dictionary<string, T>(StringComparer.Ordinal);
            var touched = false;
            foreach (var id in ids)
            {
                if (entries.TryGetValue(id, out var entry))
                {
                    found[id] = entry.Value;
                    touched |= Touch(entries, id);
                }
            }

            if (touched)
            {
                Save(entries);
            }

            return found;
        }
    }

    /// <summary>Keeps the value for the script (as used now) and saves; an id that is not a script id is ignored.</summary>
    protected void Put(string id, T value)
    {
        if (!IsScriptId(id))
        {
            return;
        }

        lock (_lock)
        {
            var entries = Entries();
            entries[id] = new SkuaScriptEntry<T>(value, _now());
            Save(entries);
        }
    }

    /// <summary>Drops the script's entry and saves.</summary>
    protected void Remove(string id)
    {
        lock (_lock)
        {
            var entries = Entries();
            if (entries.Remove(id))
            {
                Save(entries);
            }
        }
    }

    /// <summary>A value read from the file that may be kept, possibly cleaned; null drops the entry.</summary>
    protected virtual T? Clean(T value) => value;

    private bool Touch(Dictionary<string, SkuaScriptEntry<T>> entries, string id)
    {
        var now = _now();
        var entry = entries[id];
        if (now - entry.LastUsed < TouchGranularity)
        {
            return false;
        }

        entries[id] = entry with { LastUsed = now };
        return true;
    }

    private Dictionary<string, SkuaScriptEntry<T>> Entries()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        var entries = new Dictionary<string, SkuaScriptEntry<T>>(StringComparer.Ordinal);
        if (_file.Read() is { } text)
        {
            try
            {
                if (JsonSerializer.Deserialize(text, _typeInfo)?.Scripts is { } scripts)
                {
                    var oldest = _now() - Expiry;
                    foreach (var (id, entry) in scripts)
                    {
                        if (IsScriptId(id) && entry is { Value: not null } && entry.LastUsed >= oldest && Clean(entry.Value) is { } value)
                        {
                            entries[id] = entry with { Value = value };
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                // A damaged file is no entries; it is replaced on the next save.
            }
        }

        _entries = entries;
        return entries;
    }

    private void Save(Dictionary<string, SkuaScriptEntry<T>> entries)
    {
        var oldest = _now() - Expiry;
        foreach (var id in entries.Where(e => e.Value.LastUsed < oldest).Select(e => e.Key).ToList())
        {
            entries.Remove(id);
        }

        if (entries.Count > MaxEntries)
        {
            foreach (var id in entries.OrderBy(e => e.Value.LastUsed).Take(entries.Count - MaxEntries).Select(e => e.Key).ToList())
            {
                entries.Remove(id);
            }
        }

        if (_quiesced)
        {
            return;
        }

        try
        {
            _file.Write(JsonSerializer.Serialize(new SkuaScriptFileModel<T>(1, entries), _typeInfo));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Nothing that can be written is lost from memory.
        }
    }

    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ScriptIdPattern();
}

/// <summary>
/// The owner's per-script choice "ask for options when starting this script" (<c>skua-script-prefs.json</c>). Default: ask.
/// Only scripts the owner told not to ask are kept.
/// </summary>
public sealed class SkuaScriptPrefs(ISkuaScriptFile file, Func<DateTimeOffset> now)
    : SkuaScriptBook<bool>(file, now, SkuaScriptBookJson.Default.SkuaScriptFileModelBoolean)
{
    public override string Name => "Skua script preferences";

    /// <summary>True (the default) unless the owner chose not to be asked for this script.</summary>
    public bool AskOptions(string scriptId) => !TryGet(scriptId, out var ask) || ask;

    /// <summary>The choice for each of these scripts.</summary>
    public IReadOnlyDictionary<string, bool> AskOptions(IReadOnlyList<string> scriptIds)
    {
        var kept = Find(scriptIds);
        return scriptIds.Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => !kept.TryGetValue(id, out var ask) || ask, StringComparer.Ordinal);
    }

    public void Set(string scriptId, bool askOptions)
    {
        if (askOptions)
        {
            Remove(scriptId);
        }
        else
        {
            Put(scriptId, false);
        }
    }

    protected override bool Clean(bool value) => value;
}

/// <summary>
/// The option definitions each script declared the last time the plugin reported them (<c>skua-script-options.json</c>),
/// so SkuaLink can show and check a script's options after Skua restarted (the plugin then knows only the file's values).
/// </summary>
public sealed class SkuaOptionCache(ISkuaScriptFile file, Func<DateTimeOffset> now)
    : SkuaScriptBook<List<SkuaOptionDef>>(file, now, SkuaScriptBookJson.Default.SkuaScriptFileModelListSkuaOptionDef)
{
    /// <summary>At most this many definitions are kept for one script.</summary>
    public const int MaxDefinitions = 500;

    public override string Name => "Skua script options";

    /// <summary>The cached definitions of the script (an empty list: it has no options), or null when none are known.</summary>
    public IReadOnlyList<SkuaOptionDef>? Get(string scriptId) => TryGet(scriptId, out var defs) ? defs : null;

    /// <summary>Keeps the definitions the plugin reported for the script.</summary>
    public void Set(string scriptId, IReadOnlyList<SkuaOptionDef> defs)
    {
        if (Clean([.. defs]) is { } clean)
        {
            Put(scriptId, clean);
        }
    }

    protected override List<SkuaOptionDef>? Clean(List<SkuaOptionDef> value)
    {
        var kept = new List<SkuaOptionDef>();
        foreach (var def in value.Take(MaxDefinitions))
        {
            if (def is null || string.IsNullOrEmpty(def.Category) || string.IsNullOrEmpty(def.Name)
                || def.DisplayName is null || def.Description is null || def.Type is null || def.Default is null
                || (def.Choices is { } choices && choices.Any(c => c is null)))
            {
                continue;
            }

            kept.Add(SkuaProtocol.OptionTypes.Contains(def.Type) ? def : def with { Type = "other" });
        }

        return kept;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SkuaScriptFileModel<bool>))]
[JsonSerializable(typeof(SkuaScriptFileModel<List<SkuaOptionDef>>))]
internal sealed partial class SkuaScriptBookJson : JsonSerializerContext
{
}
