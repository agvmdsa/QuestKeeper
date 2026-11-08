using System.Buffers.Binary;

namespace AqwCompanion.Core.Capture;

/// <summary>
/// The pcap file adapter of the packet source seam: reads a classic pcap stream (microsecond or nanosecond,
/// either byte order) one record at a time and delivers the game-port TCP segments.
/// </summary>
public sealed class PcapStreamSource(Stream pcap) : IPacketSource
{
    private const int GlobalHeaderLength = 24;
    private const int RecordHeaderLength = 16;
    private const int MaxRecordLength = 1 << 18;

    /// <inheritdoc />
    public async Task RunAsync(Action<TcpSegment> onSegment, CancellationToken cancellationToken)
    {
        try
        {
            var header = new byte[GlobalHeaderLength];
            if (!await ReadExactAsync(header, cancellationToken))
            {
                return;
            }

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            bool bigEndian;
            switch (magic)
            {
                case 0xA1B2C3D4 or 0xA1B23C4D:
                    bigEndian = false;
                    break;
                case 0xD4C3B2A1 or 0x4D3CB2A1:
                    bigEndian = true;
                    break;
                default:
                    return;
            }

            var linkType = (int)ReadUInt32(header.AsSpan(20), bigEndian);
            var recordHeader = new byte[RecordHeaderLength];
            var data = new byte[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await ReadExactAsync(recordHeader, cancellationToken))
                {
                    return;
                }

                var length = ReadUInt32(recordHeader.AsSpan(8), bigEndian);
                if (length > MaxRecordLength)
                {
                    return;
                }
                if (data.Length < length)
                {
                    data = new byte[(int)length];
                }
                if (!await ReadExactAsync(data.AsMemory(0, (int)length), cancellationToken))
                {
                    return;
                }

                if (FrameParser.TryParse(linkType, data.AsSpan(0, (int)length), out var segment)
                    && (GameTraffic.IsGamePort(segment.SourcePort) || GameTraffic.IsGamePort(segment.DestinationPort)))
                {
                    onSegment(segment);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);

    private async ValueTask<bool> ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await pcap.ReadAsync(buffer[read..], cancellationToken);
            if (n == 0)
            {
                return false;
            }
            read += n;
        }
        return true;
    }
}
