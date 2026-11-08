using System.Buffers.Binary;

namespace AqwCompanion.Core.Capture;

/// <summary>Reads the TCP segment out of a captured link-layer frame.</summary>
public static class FrameParser
{
    private const int EthernetHeaderLength = 14;
    private const int VlanTagLength = 4;
    private const int BsdLoopbackHeaderLength = 4;
    private const ushort EtherTypeIpv4 = 0x0800;
    private const ushort EtherTypeVlan = 0x8100;
    private const int ProtocolTcp = 6;

    /// <summary>
    /// linkType is the pcap link type: 1 Ethernet, 0 BSD loopback (4-byte header), 12 and 101 raw IP.
    /// Handles one 802.1Q VLAN tag. IPv4 and TCP only; anything else, any malformed or truncated frame returns false.
    /// Never throws. The returned segment is NOT filtered by port.
    /// </summary>
    public static bool TryParse(int linkType, ReadOnlySpan<byte> frame, out TcpSegment segment)
    {
        segment = null!;
        int ipStart;
        switch (linkType)
        {
            case 1:
                if (!TryEthernetPayloadStart(frame, out ipStart))
                {
                    return false;
                }
                break;
            case 0:
                ipStart = BsdLoopbackHeaderLength;
                break;
            case 12:
            case 101:
                ipStart = 0;
                break;
            default:
                return false;
        }

        return ipStart <= frame.Length && TryParseIpv4(frame[ipStart..], out segment);
    }

    private static bool TryEthernetPayloadStart(ReadOnlySpan<byte> frame, out int start)
    {
        start = EthernetHeaderLength;
        if (frame.Length < EthernetHeaderLength)
        {
            return false;
        }

        var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame[12..]);
        if (etherType == EtherTypeVlan)
        {
            if (frame.Length < EthernetHeaderLength + VlanTagLength)
            {
                return false;
            }
            start += VlanTagLength;
            etherType = BinaryPrimitives.ReadUInt16BigEndian(frame[16..]);
        }
        return etherType == EtherTypeIpv4;
    }

    private static bool TryParseIpv4(ReadOnlySpan<byte> ip, out TcpSegment segment)
    {
        segment = null!;
        if (ip.Length < 20 || ip[0] >> 4 != 4)
        {
            return false;
        }

        var ipHeaderLength = (ip[0] & 0x0F) * 4;
        if (ipHeaderLength < 20 || ip.Length < ipHeaderLength || ip[9] != ProtocolTcp)
        {
            return false;
        }

        var fragmentOffset = BinaryPrimitives.ReadUInt16BigEndian(ip[6..]) & 0x1FFF;
        if (fragmentOffset != 0)
        {
            return false;
        }

        // The total length drops link-layer padding; a snapped (shorter) capture is read as far as it goes.
        var totalLength = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(ip[2..]), ip.Length);
        if (totalLength < ipHeaderLength + 20)
        {
            return false;
        }

        var tcp = ip[ipHeaderLength..totalLength];
        var tcpHeaderLength = (tcp[12] >> 4) * 4;
        if (tcpHeaderLength < 20 || tcp.Length < tcpHeaderLength)
        {
            return false;
        }

        segment = new TcpSegment(
            ip.Slice(12, 4).ToArray(),
            BinaryPrimitives.ReadUInt16BigEndian(tcp),
            ip.Slice(16, 4).ToArray(),
            BinaryPrimitives.ReadUInt16BigEndian(tcp[2..]),
            BinaryPrimitives.ReadUInt32BigEndian(tcp[4..]),
            (tcp[13] & 0x02) != 0,
            tcp[tcpHeaderLength..].ToArray(),
            (tcp[13] & 0x01) != 0,
            (tcp[13] & 0x04) != 0);
        return true;
    }
}
