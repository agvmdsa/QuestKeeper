using System.Text;

namespace AqwCompanion.Core.Capture;

/// <summary>
/// Reads the command name of a plain JSON message WITHOUT parsing it: the first <c>"cmd":"name"</c> within the first
/// <see cref="MaxScanBytes"/> bytes, where the name is 1 to <see cref="MaxNameLength"/> characters out of A-Z, a-z, 0-9,
/// <c>_ + . -</c>. Anything else gives null. Used on live connections so a message is only parsed when its command is one the
/// app handles.
/// </summary>
public static class LiveCommandScan
{
    /// <summary>How far into the message the name is looked for.</summary>
    public const int MaxScanBytes = 256;

    /// <summary>The longest name accepted.</summary>
    public const int MaxNameLength = 40;

    private static readonly byte[] Prefix = "\"cmd\":\""u8.ToArray();

    /// <summary>The command name in the first bytes of the message, or null.</summary>
    public static string? ReadName(ReadOnlySpan<byte> message)
    {
        var head = message[..Math.Min(message.Length, MaxScanBytes)];
        var at = head.IndexOf(Prefix);
        if (at < 0)
        {
            return null;
        }

        var start = at + Prefix.Length;
        for (var i = start; i < head.Length; i++)
        {
            var c = head[i];
            if (c == (byte)'"')
            {
                var length = i - start;
                return length is >= 1 and <= MaxNameLength ? Encoding.ASCII.GetString(head.Slice(start, length)) : null;
            }

            var allowed = c is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
                or (byte)'_' or (byte)'+' or (byte)'.' or (byte)'-';
            if (!allowed || i - start >= MaxNameLength)
            {
                return null;
            }
        }

        return null;
    }
}
