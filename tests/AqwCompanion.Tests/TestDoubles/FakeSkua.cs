using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A Skua hub for the phone API: a settable view, and commands answered by <see cref="Answer"/> (default: ok, nothing).</summary>
public sealed class FakeSkua : ISkuaFeed, ISkuaCommandChannel
{
    public SkuaHubView Current { get; set; } = SkuaHubView.Empty;

    public event EventHandler? Changed;

    public List<(string InstanceId, SkuaCommand Command)> Sent { get; } = [];

    public Func<string, SkuaCommand, Task<ResultMessage?>> Answer { get; set; } =
        (_, c) => Task.FromResult<ResultMessage?>(new ResultMessage(c.ReqId, true, null, null, null, null, false, false));

    public Task<ResultMessage?> SendCommandAsync(string instanceId, SkuaCommand command, CancellationToken ct)
    {
        Sent.Add((instanceId, command));
        return Answer(instanceId, command);
    }

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public static SkuaInstanceView Instance(string id, string label, bool disconnected = false, SkuaStatus? status = null, IReadOnlyList<SkuaLogEntry>? log = null) =>
        new(id, label, disconnected ? SkuaConnection.NotConnected : SkuaConnection.Connected, null, status, log ?? [], disconnected);

    /// <summary>Answers every command with this result (its req id replaced).</summary>
    public void AnswerWith(ResultMessage result) =>
        Answer = (_, c) => Task.FromResult<ResultMessage?>(result with { ReqId = c.ReqId });
}

/// <summary>An in-memory <see cref="ISkuaScriptFile"/>.</summary>
public sealed class MemorySkuaScriptFile(string? text = null) : ISkuaScriptFile
{
    public string? Text { get; set; } = text;

    public int Writes { get; private set; }

    public int Deletes { get; private set; }

    public string? Read() => Text;

    public void Write(string text)
    {
        Text = text;
        Writes++;
    }

    public void Delete()
    {
        Text = null;
        Deletes++;
    }
}
