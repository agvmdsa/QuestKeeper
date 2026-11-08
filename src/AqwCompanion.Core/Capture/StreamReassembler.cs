using System.Runtime.InteropServices;

namespace AqwCompanion.Core.Capture;

/// <summary>
/// Rebuilds the byte stream of ONE direction (server to client) of ONE connection from its TCP segments and cuts it into
/// NUL-terminated messages. Tolerates retransmitted and out-of-order segments; all sequence arithmetic is modular 32-bit.
/// Keeps no more than <see cref="MaxBufferBytes"/> and never throws for any input.
/// </summary>
public sealed class StreamReassembler
{
    public const int MaxBufferBytes = 4 * 1024 * 1024;
    public const int MaxMessageBytes = 2 * 1024 * 1024;
    public const int MaxPendingSegments = 200;

    private const uint HalfRange = 0x80000000;

    private readonly List<byte> _buffer = [];
    private readonly Dictionary<uint, byte[]> _pending = [];
    private long _pendingBytes;
    private uint _next;
    private bool _started;
    private bool _discarding;

    /// <summary>Feeds a segment; returns the complete NUL-terminated messages it finished (without the NUL, empty ones skipped).</summary>
    public IReadOnlyList<byte[]> Feed(TcpSegment segment)
    {
        var messages = new List<byte[]>();
        if (segment.IsSyn)
        {
            Reset();
            _next = unchecked(segment.Sequence + 1);
            _started = true;
            return messages;
        }

        var payload = segment.Payload;
        if (payload.Length == 0)
        {
            return messages;
        }

        var sequence = segment.Sequence;
        if (!_started)
        {
            _next = sequence;
            _started = true;
        }

        var diff = unchecked(sequence - _next);
        if (diff != 0 && diff >= HalfRange)
        {
            // Behind the expected position: a retransmission, possibly with new bytes at its end.
            var overlap = unchecked(_next - sequence);
            if (overlap >= payload.Length)
            {
                return messages;
            }
            payload = payload[(int)overlap..];
            sequence = _next;
            diff = 0;
        }

        if (diff != 0)
        {
            Hold(sequence, payload);
            if (_pending.Count <= MaxPendingSegments && _pendingBytes <= MaxBufferBytes)
            {
                return messages;
            }

            // A real gap: resume at the closest held segment and drop the partial message in the buffer.
            var closest = _pending.Keys.MinBy(k => unchecked(k - _next));
            payload = Take(closest);
            sequence = closest;
            _buffer.Clear();
            _discarding = false;
            _next = sequence;
        }

        Append(payload, messages);
        DrainPending(messages);
        return messages;
    }

    private void Reset()
    {
        _buffer.Clear();
        _pending.Clear();
        _pendingBytes = 0;
        _discarding = false;
    }

    private void Hold(uint sequence, byte[] payload)
    {
        if (_pending.TryGetValue(sequence, out var existing))
        {
            if (existing.Length >= payload.Length)
            {
                return;
            }
            _pendingBytes -= existing.Length;
        }
        _pending[sequence] = payload;
        _pendingBytes += payload.Length;
    }

    private byte[] Take(uint sequence)
    {
        var payload = _pending[sequence];
        _pending.Remove(sequence);
        _pendingBytes -= payload.Length;
        return payload;
    }

    private void DrainPending(List<byte[]> messages)
    {
        while (_pending.Count > 0)
        {
            uint? ready = null;
            foreach (var key in _pending.Keys)
            {
                var diff = unchecked(key - _next);
                if (diff == 0 || diff >= HalfRange)
                {
                    ready = key;
                    break;
                }
            }

            if (ready is not { } sequence)
            {
                return;
            }

            var piece = Take(sequence);
            var overlap = unchecked(_next - sequence);
            if (overlap < piece.Length)
            {
                Append(piece.AsSpan((int)overlap), messages);
            }
        }
    }

    private void Append(ReadOnlySpan<byte> data, List<byte[]> messages)
    {
        _next = unchecked(_next + (uint)data.Length);
        while (!data.IsEmpty)
        {
            var nul = data.IndexOf((byte)0);
            if (nul < 0)
            {
                if (!_discarding)
                {
                    if (_buffer.Count + data.Length > MaxMessageBytes)
                    {
                        // No NUL within the largest message allowed: drop it and skip to the next NUL.
                        _buffer.Clear();
                        _discarding = true;
                    }
                    else
                    {
                        _buffer.AddRange(data);
                    }
                }
                return;
            }

            var piece = data[..nul];
            data = data[(nul + 1)..];
            if (_discarding)
            {
                _discarding = false;
                continue;
            }

            if (_buffer.Count + piece.Length <= MaxMessageBytes && _buffer.Count + piece.Length > 0)
            {
                var message = new byte[_buffer.Count + piece.Length];
                CollectionsMarshal.AsSpan(_buffer).CopyTo(message);
                piece.CopyTo(message.AsSpan(_buffer.Count));
                messages.Add(message);
            }
            _buffer.Clear();
        }
    }
}
