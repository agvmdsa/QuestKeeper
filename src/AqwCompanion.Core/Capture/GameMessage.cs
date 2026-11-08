using System.Text.Json;

namespace AqwCompanion.Core.Capture;

/// <summary>One decoded game message: its command and the parsed JSON, which lives only in memory until disposed.</summary>
public sealed class GameMessage : IDisposable
{
    internal GameMessage(string command, JsonDocument document)
    {
        Command = command;
        Document = document;
    }

    /// <summary>The command name, the string at <c>b.o.cmd</c>.</summary>
    public string Command { get; }

    /// <summary>The parsed message. Not usable after <see cref="Dispose"/>.</summary>
    public JsonDocument Document { get; }

    /// <inheritdoc />
    public void Dispose() => Document.Dispose();
}
