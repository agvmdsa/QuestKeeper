namespace AqwCompanion.Phone.Skua;

/// <summary>
/// One Skua as the hub keeps it: its public id, its order of arrival, every plugin id that belongs to it, the bridge that
/// holds its status and log, the character its log belongs to, and the pipe it is attached to (null once disconnected).
/// Only touched under the hub's lock.
/// </summary>
internal sealed class SkuaInstance(string id, int number, SkuaBridge bridge)
{
    public string Id { get; } = id;

    public int Number { get; } = number;

    /// <summary>The plugin ids that reattach this instance: its own, plus those of the connections that took it over.</summary>
    public HashSet<string> PluginIds { get; } = new(StringComparer.Ordinal) { id };

    public SkuaBridge Bridge { get; set; } = bridge;

    /// <summary>The character the log belongs to; null until the first status that names one.</summary>
    public string? Character { get; set; }

    public object? Attachment { get; set; }

    public bool Disconnected => Attachment is null;

    public string Label => Character ?? $"Skua {Number}";

    public SkuaInstanceView View()
    {
        var view = Bridge.Current;
        return new SkuaInstanceView(Id, Label, view.Connection, view.Hello, view.Status, view.Log, Disconnected);
    }
}
