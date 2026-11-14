using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Skua;

public enum SkuaConnection { NotConnected, Connected, VersionMismatch }

/// <summary>What the Skua tab shows for one Skua. The log is newest first.</summary>
public sealed record SkuaView(SkuaConnection Connection, SkuaHello? Hello, SkuaStatus? Status, IReadOnlyList<SkuaLogEntry> Log)
{
    public static SkuaView Empty { get; } = new(SkuaConnection.NotConnected, null, null, []);
}

/// <summary>
/// One Skua SkuaLink knows. <paramref name="Label"/> is the character name, or <c>Skua N</c> (order of arrival) while no
/// character is known. <paramref name="Disconnected"/> is true once its pipe closed; the instance stays until SkuaLink restarts.
/// The log is newest first.
/// </summary>
public sealed record SkuaInstanceView(
    string InstanceId, string Label, SkuaConnection Connection, SkuaHello? Hello, SkuaStatus? Status, IReadOnlyList<SkuaLogEntry> Log, bool Disconnected)
{
    public SkuaView ToView() => new(Connection, Hello, Status, Log);
}

/// <summary>Every Skua SkuaLink knows, in order of arrival.</summary>
public sealed record SkuaHubView(IReadOnlyList<SkuaInstanceView> Instances)
{
    public static SkuaHubView Empty { get; } = new([]);

    /// <summary>
    /// The single view for the parts that show one Skua (the phone state's <c>skua</c> field and the Settings line): the first
    /// connected instance, else the first one, else <see cref="SkuaView.Empty"/>.
    /// </summary>
    public SkuaView Primary =>
        (Instances.FirstOrDefault(i => !i.Disconnected) ?? Instances.FirstOrDefault())?.ToView() ?? SkuaView.Empty;
}

/// <summary>
/// One Skua as the phone API and the state's <c>skuaInstances</c> show it: the instance with at most
/// <see cref="MaxLog"/> log lines (newest first). The status carries <c>control</c>, <c>settleSecondsLeft</c> and
/// <c>optionsStuck</c>.
/// </summary>
public sealed record SkuaInstanceState(
    string InstanceId, string Label, SkuaConnection Connection, bool Disconnected, SkuaHello? Hello, SkuaStatus? Status, IReadOnlyList<SkuaLogEntry> Log)
{
    public const int MaxLog = 100;

    public static SkuaInstanceState From(SkuaInstanceView view) =>
        new(view.InstanceId, view.Label, view.Connection, view.Disconnected, view.Hello, view.Status, [.. view.Log.Take(MaxLog)]);

    public static IReadOnlyList<SkuaInstanceState> From(SkuaHubView view) => [.. view.Instances.Select(From)];
}
