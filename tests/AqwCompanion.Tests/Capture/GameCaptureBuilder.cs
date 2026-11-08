using System.IO.Compression;
using System.Text;
using System.Text.Json;
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

    /// <summary>The message envelope the game uses: <c>{"t":"xt","b":{"r":-1,"o":{"cmd":..., ...}}}</c>.</summary>
    public static Dictionary<string, object?> Envelope(string cmd, IDictionary<string, object?>? body = null)
    {
        var o = new Dictionary<string, object?> { ["cmd"] = cmd };
        foreach (var (key, value) in body ?? new Dictionary<string, object?>())
        {
            o[key] = value;
        }
        return new Dictionary<string, object?>
        {
            ["t"] = "xt",
            ["b"] = new Dictionary<string, object?> { ["r"] = -1, ["o"] = o },
        };
    }

    /// <summary>The JSON text of the object as bytes, without a terminator.</summary>
    public static byte[] PlainJson(object json) => JsonSerializer.SerializeToUtf8Bytes(json);

    /// <summary>An encoded blob: marker characters, then base64 of the zlib (or gzip) stream of the JSON text.</summary>
    public static byte[] EncodeBlob(object json, int markerChars = 1, bool stripPadding = false, bool gzip = false) =>
        EncodeBlobText(PlainJson(json), markerChars, stripPadding, gzip);

    public static byte[] EncodeBlobText(byte[] content, int markerChars = 1, bool stripPadding = false, bool gzip = false)
    {
        var base64 = Convert.ToBase64String(Compress(content, gzip));
        if (stripPadding)
        {
            base64 = base64.TrimEnd('=');
        }
        return Encoding.ASCII.GetBytes(new string('Z', markerChars) + base64);
    }

    public static byte[] Compress(byte[] content, bool gzip = false)
    {
        using var output = new MemoryStream();
        using (Stream compressor = gzip
            ? new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
            : new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressor.Write(content);
        }
        return output.ToArray();
    }
}
