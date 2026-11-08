using System.Text;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Capture;

/// <summary>Builds fictional game traffic for the capture pipeline tests.</summary>
internal static class GameCaptureBuilder
{
    public const int GamePort = 5588;
    public const int ClientPort = 50000;

    /// <summary>A server-to-client data segment.</summary>
    public static TcpSegment ServerData(uint sequence, byte[] payload, int clientPort = ClientPort) =>
        new(PcapBuilder.ServerAddress, GamePort, PcapBuilder.ClientAddress, clientPort, sequence, false, payload);

    /// <summary>The server's SYN-ACK; the first data byte has sequence <paramref name="sequence"/> + 1.</summary>
    public static TcpSegment SynAck(uint sequence, int clientPort = ClientPort) =>
        new(PcapBuilder.ServerAddress, GamePort, PcapBuilder.ClientAddress, clientPort, sequence, true, []);

    /// <summary>A client-to-server data segment.</summary>
    public static TcpSegment ClientData(uint sequence, byte[] payload, int clientPort = ClientPort) =>
        new(PcapBuilder.ClientAddress, clientPort, PcapBuilder.ServerAddress, GamePort, sequence, false, payload);

    /// <summary>The text as bytes followed by a NUL.</summary>
    public static byte[] Terminated(string text) => [.. Encoding.UTF8.GetBytes(text), 0];

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
