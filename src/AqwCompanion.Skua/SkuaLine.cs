using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AqwCompanion.Skua;

/// <summary>One JSON object per line. Never throws on input; <see cref="Parse"/> answers null for anything it cannot use.</summary>
public static class SkuaLine
{
    public static string ToLine(SkuaMessage message)
    {
        if (message is not StatusMessage { Status.Quests: { Count: > 0 } quests })
        {
            return Write(message, int.MaxValue);
        }

        // Quest names are cut, but 30 long names in escaped non-ASCII could still pass the line cap, and a line over the
        // cap is refused whole. The last quests are left out until the line fits.
        var count = Math.Min(quests.Count, SkuaProtocol.MaxQuests);
        var line = Write(message, count);
        while (count > 0 && Encoding.UTF8.GetByteCount(line) > SkuaProtocol.MaxLineBytes)
        {
            line = Write(message, --count);
        }

        return line;
    }

    private static string Write(SkuaMessage message, int maxQuests)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", SkuaProtocol.Version);
            switch (message)
            {
                case HelloMessage h:
                    w.WriteString("type", "hello");
                    w.WriteString("plugin", Cut(h.Hello.PluginVersion));
                    w.WriteString("skua", Cut(h.Hello.SkuaVersion));
                    break;
                case StatusMessage s:
                    w.WriteString("type", "status");
                    w.WriteBoolean("loggedIn", s.Status.LoggedIn);
                    WriteNullable(w, "character", s.Status.Character);
                    WriteNullable(w, "server", s.Status.Server);
                    WriteNullable(w, "map", s.Status.Map);
                    WriteNullable(w, "script", s.Status.Script);
                    w.WriteBoolean("running", s.Status.ScriptRunning);
                    if (s.Status.RunningSince is { } since)
                    {
                        w.WriteString("runningSince", since.ToString("o", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        w.WriteNull("runningSince");
                    }

                    if (s.Status.Quests is { } quests)
                    {
                        w.WriteStartArray("quests");
                        foreach (var quest in quests.Take(Math.Min(maxQuests, SkuaProtocol.MaxQuests)))
                        {
                            w.WriteStartObject();
                            w.WriteNumber("id", quest.Id);
                            w.WriteString("name", Cut(quest.Name));
                            w.WriteBoolean("ready", quest.Ready);
                            w.WriteEndObject();
                        }

                        w.WriteEndArray();
                    }

                    break;
                case LogMessage l:
                    w.WriteString("type", "log");
                    w.WriteString("at", l.Entry.At.ToString("o", CultureInfo.InvariantCulture));
                    w.WriteString("kind", l.Entry.Kind.ToString());
                    w.WriteString("text", Cut(l.Entry.Text));
                    break;
                default:
                    throw new ArgumentException("This message is never sent.", nameof(message));
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static SkuaMessage? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || Encoding.UTF8.GetByteCount(line) > SkuaProtocol.MaxLineBytes)
        {
            return null;
        }

        if (HasLoneSurrogate(line))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || !v.TryGetInt32(out var version))
            {
                return null;
            }

            if (version != SkuaProtocol.Version)
            {
                return new UnsupportedVersionMessage(version);
            }

            return Str(root, "type") switch
            {
                "hello" => Str(root, "plugin") is { } plugin && Str(root, "skua") is { } skua
                    ? new HelloMessage(new SkuaHello(Cut(plugin), Cut(skua)))
                    : null,
                "status" => ParseStatus(root),
                "log" => ParseLog(root),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static SkuaMessage? ParseStatus(JsonElement root)
    {
        if (!root.TryGetProperty("loggedIn", out var loggedIn) || loggedIn.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("running", out var running) || running.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        DateTimeOffset? since = null;
        if (Str(root, "runningSince") is { } text)
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return null;
            }

            since = parsed;
        }

        return new StatusMessage(new SkuaStatus(
            loggedIn.GetBoolean(), CutOrNull(Str(root, "character")), CutOrNull(Str(root, "server")), CutOrNull(Str(root, "map")), CutOrNull(Str(root, "script")),
            running.GetBoolean(), since, ParseQuests(root)));
    }

    /// <summary>
    /// The active quests, or null when the field is absent or not an array (an older plugin, or damage: the rest of the
    /// status still counts). A damaged entry is left out on its own; at most <see cref="SkuaProtocol.MaxQuests"/> are kept.
    /// </summary>
    private static IReadOnlyList<SkuaQuest>? ParseQuests(JsonElement root)
    {
        if (!root.TryGetProperty("quests", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var quests = new List<SkuaQuest>();
        foreach (var entry in array.EnumerateArray())
        {
            if (quests.Count >= SkuaProtocol.MaxQuests)
            {
                break;
            }

            if (ParseQuest(entry) is { } quest)
            {
                quests.Add(quest);
            }
        }

        return quests;
    }

    private static SkuaQuest? ParseQuest(JsonElement entry)
    {
        try
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var questId) || questId < 0
                || Str(entry, "name") is not { } name
                || !entry.TryGetProperty("ready", out var ready) || ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            return new SkuaQuest(questId, Cut(name), ready.GetBoolean());
        }
        catch (InvalidOperationException)
        {
            // An escaped lone surrogate in the name: only this entry is lost.
            return null;
        }
    }

    private static SkuaMessage? ParseLog(JsonElement root)
    {
        if (Str(root, "at") is not { } at
            || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            || Str(root, "kind") is not { } kindText
            || Array.IndexOf(Enum.GetNames<SkuaLogKind>(), kindText) < 0
            || Str(root, "text") is not { } body)
        {
            return null;
        }

        var kind = Enum.Parse<SkuaLogKind>(kindText);
        return new LogMessage(new SkuaLogEntry(time, kind, Cut(body)));
    }

    /// <summary>
    /// A string value, or null when absent, not a string, or containing U+FFFD (which the plugin never sends). An escaped
    /// lone surrogate (<c>\ud800</c>) makes <c>GetString</c> throw <see cref="InvalidOperationException"/>: the caller
    /// catches it (<see cref="Parse"/> for the whole line, <c>ParseQuest</c> for one quest entry).
    /// </summary>
    private static string? Str(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = p.GetString();
        return text is null || text.Contains('\uFFFD') ? null : text;
    }

    private static void WriteNullable(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
        {
            w.WriteNull(name);
        }
        else
        {
            w.WriteString(name, Cut(value));
        }
    }

    private static string Cut(string value)
    {
        if (value.Length <= SkuaProtocol.MaxTextLength)
        {
            return value;
        }

        var length = SkuaProtocol.MaxTextLength;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    private static string? CutOrNull(string? value) => value is null ? null : Cut(value);
}
