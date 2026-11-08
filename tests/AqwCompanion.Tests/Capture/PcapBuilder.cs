using System.Buffers.Binary;

namespace AqwCompanion.Tests.Capture;

/// <summary>Builds real Ethernet/loopback/raw IPv4 TCP frames and in-memory classic pcap files for tests.</summary>
internal static class PcapBuilder
{
    public const int Ethernet = 1;
    public const int BsdLoopback = 0;
    public const int RawIp = 101;
    public const int RawIpBsd = 12;

    public static readonly byte[] ClientAddress = [192, 168, 1, 20];
    public static readonly byte[] ServerAddress = [34, 120, 8, 9];

    public static byte[] TcpFrame(
        int srcPort = 50000,
        int dstPort = 5588,
        uint sequence = 1000,
        bool syn = false,
        byte[]? payload = null,
        int linkType = Ethernet,
        bool vlan = false,
        int ipOptionBytes = 0,
        int tcpOptionBytes = 0,
        byte[]? srcAddress = null,
        byte[]? dstAddress = null,
        byte protocol = 6,
        int fragmentOffset = 0,
        int ethernetPadding = 0)
    {
        payload ??= [];
        var ipHeaderLength = 20 + ipOptionBytes;
        var tcpHeaderLength = 20 + tcpOptionBytes;
        var totalLength = ipHeaderLength + tcpHeaderLength + payload.Length;

        using var ms = new MemoryStream();
        switch (linkType)
        {
            case Ethernet:
                ms.Write([0x02, 0, 0, 0, 0, 0x01, 0x02, 0, 0, 0, 0, 0x02]);
                if (vlan)
                {
                    ms.Write([0x81, 0x00, 0x00, 0x64]);
                }
                ms.Write([0x08, 0x00]);
                break;
            case BsdLoopback:
                ms.Write([2, 0, 0, 0]);
                break;
        }

        var ip = new byte[ipHeaderLength];
        ip[0] = (byte)(0x40 | (ipHeaderLength / 4));
        BinaryPrimitives.WriteUInt16BigEndian(ip.AsSpan(2), (ushort)totalLength);
        BinaryPrimitives.WriteUInt16BigEndian(ip.AsSpan(6), (ushort)fragmentOffset);
        ip[8] = 64;
        ip[9] = protocol;
        (srcAddress ?? ClientAddress).CopyTo(ip, 12);
        (dstAddress ?? ServerAddress).CopyTo(ip, 16);
        for (var i = 20; i < ipHeaderLength; i++)
        {
            ip[i] = 0x01; // NOP options
        }
        ms.Write(ip);

        var tcp = new byte[tcpHeaderLength];
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(0), (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.AsSpan(4), sequence);
        tcp[12] = (byte)((tcpHeaderLength / 4) << 4);
        tcp[13] = (byte)(syn ? 0x02 : 0x18);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(14), 65535);
        for (var i = 20; i < tcpHeaderLength; i++)
        {
            tcp[i] = 0x01;
        }
        ms.Write(tcp);
        ms.Write(payload);
        ms.Write(new byte[ethernetPadding]);
        return ms.ToArray();
    }

    public static byte[] Pcap(
        IEnumerable<byte[]> frames,
        int linkType = Ethernet,
        bool bigEndian = false,
        bool nanosecond = false)
    {
        using var ms = new MemoryStream();
        WriteGlobalHeader(ms, linkType, bigEndian, nanosecond);
        foreach (var frame in frames)
        {
            WriteRecord(ms, frame, bigEndian);
        }
        return ms.ToArray();
    }

    public static void WriteGlobalHeader(Stream s, int linkType, bool bigEndian, bool nanosecond)
    {
        var header = new byte[24];
        var magic = nanosecond ? 0xA1B23C4Du : 0xA1B2C3D4u;
        Write32(header, 0, magic, bigEndian);
        Write16(header, 4, 2, bigEndian);
        Write16(header, 6, 4, bigEndian);
        Write32(header, 16, 65535, bigEndian);
        Write32(header, 20, (uint)linkType, bigEndian);
        s.Write(header);
    }

    public static void WriteRecord(Stream s, byte[] frame, bool bigEndian)
    {
        var header = new byte[16];
        Write32(header, 0, 1_700_000_000, bigEndian);
        Write32(header, 4, 123, bigEndian);
        Write32(header, 8, (uint)frame.Length, bigEndian);
        Write32(header, 12, (uint)frame.Length, bigEndian);
        s.Write(header);
        s.Write(frame);
    }

    private static void Write32(byte[] b, int at, uint value, bool bigEndian)
    {
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
        }
    }

    private static void Write16(byte[] b, int at, ushort value, bool bigEndian)
    {
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), value);
        }
    }
}
