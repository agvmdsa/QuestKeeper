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
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var result = new List<SkuaLogEntry>();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var entry = element.Deserialize<SkuaLogEntry>(Options);
                    if (entry is { Text: not null } && Enum.IsDefined(entry.Kind))
                    {
                        result.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Drop only the bad entry.
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Skua log load failed: {ExceptionType}", ex.GetType().Name);
            return [];
        }
    }

    public void Save(IReadOnlyList<SkuaLogEntry> log)
    {
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(log, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Skua log save failed: {ExceptionType}", ex.GetType().Name);
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temp file is harmless; the next save overwrites it.
            }
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Skua log delete failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
