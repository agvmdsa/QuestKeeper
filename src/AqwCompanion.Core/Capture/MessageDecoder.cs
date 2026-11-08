using System.Buffers.Text;
using System.IO.Compression;
using System.Text.Json;
using AqwCompanion.Core.Common;

namespace AqwCompanion.Core.Capture;

/// <summary>
/// Turns one NUL-free message of the game stream into its command and parsed JSON, in memory only. A message is plain JSON
/// (starts with <c>{</c>) or an encoded blob: 0-8 marker characters, base64 (padding may be missing), then zlib or gzip whose
/// content is JSON. Text messages (<c>%xt%</c>), XML and anything else carry no allowed data and give nothing.
/// </summary>
public static class MessageDecoder
{
    public const int MaxEncodedBytes = 2 * 1024 * 1024;
    public const int MaxInflatedBytes = 8 * 1024 * 1024;
    public const int MaxJsonDepth = 16;
    public const int MinEncodedBytes = 40;

    private const int MaxCommandLength = 64;
    private const int MaxMarkerChars = 8;
    private const int MaxDroppedSuffixChars = 3;
    private const int MinBase64Chars = 8;

    /// <summary>
    /// null when: not JSON and not an encoded blob, a bound is exceeded, the JSON is invalid or has no b.o.cmd string. Never throws.
    /// </summary>
    public static GameMessage? Decode(ReadOnlySpan<byte> message)
    {
        try
        {
            if (message.IsEmpty || message.Length > MaxEncodedBytes)
            {
                return null;
            }

            if (message[0] == (byte)'{')
            {
                return Parse(message.ToArray());
            }

            if (message.Length < MinEncodedBytes || message[0] == (byte)'%' || message[0] == (byte)'<')
            {
                return null;
            }

            var inflated = InflateBlob(message);
            return inflated is null ? null : Parse(inflated);
        }
        catch (Exception)
        {
            // No content may leave the decoder, not even in an exception.
            return null;
        }
    }

    private static GameMessage? Parse(byte[] json)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var command = ReadCommand(document.RootElement);
            if (command is null)
            {
                document.Dispose();
                return null;
            }

            return new GameMessage(command, document);
        }
        catch (JsonException)
        {
            document?.Dispose();
            return null;
        }
    }

    private static string? ReadCommand(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("b", out var b) || b.ValueKind != JsonValueKind.Object
            || !b.TryGetProperty("o", out var o) || o.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return o.ReadCleanText("cmd", MaxCommandLength);
    }

    /// <summary>Tries the marker and padding readings in the order of the validated probe; the first that inflates to JSON wins.</summary>
    private static byte[]? InflateBlob(ReadOnlySpan<byte> raw)
    {
        for (var dropLast = 0; dropLast <= MaxDroppedSuffixChars; dropLast++)
        {
            for (var dropFirst = 0; dropFirst <= MaxMarkerChars; dropFirst++)
            {
                var length = raw.Length - dropFirst - dropLast;
                if (length < MinBase64Chars)
                {
                    continue;
                }

                var blob = DecodeBase64(raw.Slice(dropFirst, length));
                if (blob is null)
                {
                    continue;
                }

                var result = TryInflate(blob);
                if (result.Overflow)
                {
                    return null;
                }

                if (result.Data is { Length: > 0 } data && data[0] == (byte)'{')
                {
                    return data;
                }
            }
        }

        return null;
    }

    private static byte[]? DecodeBase64(ReadOnlySpan<byte> text)
    {
        var missing = -text.Length & 3;
        if (missing == 3)
        {
            return null;
        }

        var padded = new byte[text.Length + missing];
        text.CopyTo(padded);
        padded.AsSpan(text.Length).Fill((byte)'=');

        var decoded = new byte[Base64.GetMaxDecodedFromUtf8Length(padded.Length)];
        return Base64.DecodeFromUtf8(padded, decoded, out _, out var written) == System.Buffers.OperationStatus.Done
            ? decoded.AsSpan(0, written).ToArray()
            : null;
    }

    private readonly record struct InflateResult(byte[]? Data, bool Overflow);

    private static InflateResult TryInflate(byte[] blob)
    {
        Stream? decompressor = null;
        try
        {
            var input = new MemoryStream(blob, writable: false);
            if (blob.Length >= 2 && blob[0] == 0x78 && blob[1] is 0x01 or 0x5E or 0x9C or 0xDA)
            {
                decompressor = new ZLibStream(input, CompressionMode.Decompress);
            }
            else if (blob.Length >= 2 && blob[0] == 0x1F && blob[1] == 0x8B)
            {
                decompressor = new GZipStream(input, CompressionMode.Decompress);
            }
            else
            {
                return default;
            }

            return ReadBounded(decompressor);
        }
        catch (InvalidDataException)
        {
            return default;
        }
        catch (IOException)
        {
            return default;
        }
        finally
        {
            decompressor?.Dispose();
        }
    }

    /// <summary>Reads at most <see cref="MaxInflatedBytes"/> bytes; one more byte means the stream is too big.</summary>
    private static InflateResult ReadBounded(Stream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var room = MaxInflatedBytes + 1 - (int)output.Length;
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, room));
            if (read == 0)
            {
                return new InflateResult(output.ToArray(), false);
            }

            output.Write(buffer, 0, read);
            if (output.Length > MaxInflatedBytes)
            {
                return new InflateResult(null, true);
            }
        }
    }
}
