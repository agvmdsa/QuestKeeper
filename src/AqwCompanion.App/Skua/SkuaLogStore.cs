using System.Text.Json;
using System.Text.Json.Serialization;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using Serilog;

namespace AqwCompanion.App.Skua;

/// <summary>Keeps the Skua log as one JSON file, written whole and moved into place. Never throws.</summary>
public sealed class SkuaLogStore(string path) : ISkuaLogStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public IReadOnlyList<SkuaLogEntry> Load()
    {
        TryLoad(out var log);
        return log;
    }

    /// <summary>False (and an empty log) when the file exists but cannot be read or is not a list; a bad entry alone is dropped.</summary>
    public bool TryLoad(out IReadOnlyList<SkuaLogEntry> log)
    {
        log = [];
        try
        {
            if (!File.Exists(path))
            {
                return !Directory.Exists(path) || Fail(new IOException("not a file"));
            }

            var result = new List<SkuaLogEntry>();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Fail(new JsonException("not a list"));
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var entry = element.Deserialize<SkuaLogEntry>(Options);
                    if (entry is { Text: not null } && Enum.IsDefined(entry.Kind))
                    {
                        result.Add(Normalized(entry));
                    }
                }
                catch (JsonException)
                {
                    // Drop only the bad entry.
                }
            }

            log = result;
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return Fail(ex);
        }
    }

    private static bool Fail(Exception ex)
    {
        Log.Error("Skua log load failed: {ExceptionType}", ex.GetType().Name);
        return false;
    }

    /// <summary>A count needs an item and stays within 1 to <see cref="SkuaProtocol.MaxCount"/>; the item is cut to the text cap.</summary>
    private static SkuaLogEntry Normalized(SkuaLogEntry entry)
    {
        if (entry.Item is not { } item)
        {
            return entry.Count == 1 ? entry : entry with { Count = 1 };
        }

        if (item.Length > SkuaProtocol.MaxTextLength)
        {
            item = item[..(char.IsHighSurrogate(item[SkuaProtocol.MaxTextLength - 1]) ? SkuaProtocol.MaxTextLength - 1 : SkuaProtocol.MaxTextLength)];
        }

        return entry with { Item = item, Count = Math.Clamp(entry.Count, 1, SkuaProtocol.MaxCount) };
    }

    public void Save(IReadOnlyList<SkuaLogEntry> log) => TrySave(log);

    public bool TrySave(IReadOnlyList<SkuaLogEntry> log)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(log, Options));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Log.Error("Skua log save failed: {ExceptionType}", ex.GetType().Name);
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (IsExpected(cleanup))
            {
                // The temp file is harmless; the next save overwrites it.
            }

            return false;
        }
    }

    public void Delete()
    {
        foreach (var file in new[] { path, path + ".tmp" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                Log.Error("Skua log delete failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException or System.Security.SecurityException;
}

/// <summary>The Skua logs in one folder: <c>skua-log.&lt;character&gt;.json</c> per character and the legacy <c>skua-log.json</c>. Never throws.</summary>
public sealed class SkuaLogStores(string folder) : ISkuaLogStores
{
    public ISkuaLogStore ForCharacter(string character) => new SkuaLogStore(Path.Combine(folder, SkuaLogFiles.FileName(character)));

    public ISkuaLogStore Legacy { get; } = new SkuaLogStore(Path.Combine(folder, SkuaLogFiles.LegacyFileName));

    /// <summary>Deletes every <c>skua-log.*.json</c> (and its <c>.tmp</c>) in the folder, the legacy log included; nothing else.</summary>
    public void DeleteAll()
    {
        List<string> files;
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            files = [.. Directory.EnumerateFiles(folder, SkuaLogFiles.Prefix + "*").Where(IsSkuaLog)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            Log.Error("Skua log delete failed: {ExceptionType}", ex.GetType().Name);
            return;
        }

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                Log.Error("Skua log delete failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    private static bool IsSkuaLog(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(SkuaLogFiles.Prefix, StringComparison.Ordinal)
            && (name.EndsWith(SkuaLogFiles.Suffix, StringComparison.Ordinal) || name.EndsWith(SkuaLogFiles.Suffix + ".tmp", StringComparison.Ordinal));
    }
}
