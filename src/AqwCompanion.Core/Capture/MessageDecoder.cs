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
    public static GameMessage? Decode(ReadOnlySpan<byte> message) => TryDecode(message, out _);

    /// <summary>
    /// Like <see cref="Decode"/>, and when the result is null says how far the decoding got: the most advanced stage reached over
    /// all marker and padding readings. <paramref name="failure"/> is <see cref="DecodeFailure.None"/> when a message is returned.
    /// Never throws and never exposes any content.
    /// </summary>
    public static GameMessage? TryDecode(ReadOnlySpan<byte> message, out DecodeFailure failure)
    {
        failure = DecodeFailure.None;
        try
        {
            if (message.IsEmpty || message[0] == (byte)'%' || message[0] == (byte)'<')
            {
                failure = DecodeFailure.NotBlob;
                return null;
            }

            if (message.Length > MaxEncodedBytes)
            {
                failure = DecodeFailure.TooLarge;
                return null;
            }

            if (message[0] == (byte)'{')
            {
                return Parse(message.ToArray(), out failure);
            }

            if (message.Length < MinEncodedBytes)
            {
                failure = DecodeFailure.NotBlob;
                return null;
            }

            var inflated = InflateBlob(message, out failure);
            return inflated is null ? null : Parse(inflated, out failure);
        }
        catch (Exception)
        {
            // No content may leave the decoder, not even in an exception.
            failure = DecodeFailure.InflateFailed;
            return null;
        }
    }

    private static GameMessage? Parse(byte[] json, out DecodeFailure failure)
    {
        failure = DecodeFailure.None;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var command = ReadCommand(document.RootElement);
            if (command is null)
            {
                document.Dispose();
                failure = DecodeFailure.NoCommand;
                return null;
            }

            return new GameMessage(command, document);
        }
        catch (JsonException)
        {
            document?.Dispose();
            failure = DecodeFailure.InvalidJson;
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

    /// <summary>
    /// Tries the marker and padding readings in the order of the validated probe; the first that inflates to JSON wins. When none
    /// does, <paramref name="failure"/> is the most advanced stage any reading reached.
    /// </summary>
    private static byte[]? InflateBlob(ReadOnlySpan<byte> raw, out DecodeFailure failure)
    {
        failure = DecodeFailure.NotBase64;
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

                failure = Advance(failure, DecodeFailure.NoCompressionHeader);
                var result = TryInflate(blob);
                if (result.Overflow)
                {
                    failure = DecodeFailure.TooBigWhenInflated;
                    return null;
                }

                if (result.HasHeader)
                {
                    failure = Advance(failure, DecodeFailure.InflateFailed);
                }

                if (result.Data is { Length: > 0 } data)
                {
                    if (data[0] == (byte)'{')
                    {
                        failure = DecodeFailure.None;
                        return data;
                    }

                    failure = Advance(failure, DecodeFailure.InflatedNotJson);
                }
            }
        }

        return null;
    }

    // The enum lists the stages in the order a message passes through them.
    private static DecodeFailure Advance(DecodeFailure current, DecodeFailure reached) => reached > current ? reached : current;

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

    private readonly record struct InflateResult(byte[]? Data, bool Overflow, bool HasHeader);

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

            return ReadBounded(decompressor) with { HasHeader = true };
        }
        catch (InvalidDataException)
        {
            return new InflateResult(null, false, true);
        }
        catch (IOException)
        {
            return new InflateResult(null, false, true);
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
                return new InflateResult(output.ToArray(), false, true);
            }

            output.Write(buffer, 0, read);
            if (output.Length > MaxInflatedBytes)
            {
                return new InflateResult(null, true, true);
            }
        }
    }
}

/// <summary>Why <see cref="MessageDecoder.TryDecode"/> gave nothing: the most advanced stage reached, in the order of the stages.</summary>
public enum DecodeFailure
{
    /// <summary>A message was decoded.</summary>
    None,

    /// <summary>Starts with <c>%</c> or <c>&lt;</c> (text and XML), is empty or is shorter than the minimum encoded size.</summary>
    NotBlob,

    /// <summary>Longer than the encoded size cap.</summary>
    TooLarge,

    /// <summary>No marker and padding reading produced valid base64.</summary>
    NotBase64,

    /// <summary>Valid base64 under some reading, but none began with a zlib or gzip header.</summary>
    NoCompressionHeader,

    /// <summary>A compression header was found but inflating failed under every reading.</summary>
    InflateFailed,

    /// <summary>The inflated size cap was hit.</summary>
    TooBigWhenInflated,

    /// <summary>Inflated, but the content does not start with <c>{</c>.</summary>
    InflatedNotJson,

    /// <summary>A JSON parse error, or deeper than the depth cap.</summary>
    InvalidJson,

    /// <summary>Valid JSON without a <c>b.o.cmd</c> string.</summary>
    NoCommand,
}
