namespace UniLinker.Core;

/// <summary>
/// RFC 6184 — RTP Payload Format for H.264 Video.
/// Handles Single NAL Unit and FU-A fragmentation packets.
/// </summary>
public class RtpPacketizer
{
    private const int Mtu = 1400;
    private const int RtpHeaderSize = 12;
    private const int FuHeaderSize = 2;
    private const int DynamicPayloadType = 96;

    private ushort _sequenceNumber;
    private readonly uint _ssrc;
    private readonly uint _timestampBase;

    public RtpPacketizer()
    {
        _ssrc = (uint)Random.Shared.Next();
        _timestampBase = (uint)Environment.TickCount;
    }

    /// <summary>
    /// Split a H.264 NAL unit into one or more RTP packets.
    /// </summary>
    /// <param name="nalUnit">Complete H.264 NAL unit including start code or NAL header</param>
    /// <param name="timestampUs">Frame timestamp in microseconds</param>
    /// <returns>One or more RTP packets ready for UDP transmission</returns>
    public List<byte[]> Packetize(byte[] nalUnit, long timestampUs)
    {
        // 90kHz clock for video
        var rtpTimestamp = (uint)(timestampUs * 90 / 1000);

        if (nalUnit.Length <= Mtu - RtpHeaderSize)
        {
            return [BuildSingleNalPacket(nalUnit, rtpTimestamp)];
        }

        return BuildFuAPackets(nalUnit, rtpTimestamp);
    }

    private byte[] BuildSingleNalPacket(byte[] nal, uint timestamp)
    {
        var seq = _sequenceNumber++;
        var packet = new byte[RtpHeaderSize + nal.Length];

        // RTP Fixed Header (RFC 3550)
        packet[0] = 0x80; // V=2, P=0, X=0, CC=0
        packet[1] = (byte)(DynamicPayloadType | 0x80); // M=1 (last packet of frame), PT=96
        WriteUInt16BE(packet, 2, seq);
        WriteUInt32BE(packet, 4, timestamp);
        WriteUInt32BE(packet, 8, _ssrc);

        Buffer.BlockCopy(nal, 0, packet, RtpHeaderSize, nal.Length);

        return packet;
    }

    private List<byte[]> BuildFuAPackets(byte[] nal, uint timestamp)
    {
        var packets = new List<byte[]>();

        // Extract NAL header info
        byte nalHeader = nal[0];
        byte forbiddenBit = (byte)(nalHeader & 0x80);
        byte nri = (byte)(nalHeader & 0x60);
        byte nalType = (byte)(nalHeader & 0x1F);

        // FU indicator: F + NRI + Type(28=FU-A)
        byte fuIndicator = (byte)(forbiddenBit | nri | 28);

        int offset = 1; // Skip original NAL header byte
        bool isFirst = true;

        while (offset < nal.Length - 1) // -1 to avoid off-by-one
        {
            int maxPayload = Mtu - RtpHeaderSize - FuHeaderSize;
            int payloadSize = Math.Min(maxPayload, nal.Length - offset);
            bool isLast = (offset + payloadSize >= nal.Length - 1);

            if (isLast)
                payloadSize = nal.Length - offset;

            var packet = new byte[RtpHeaderSize + FuHeaderSize + payloadSize];

            // RTP header
            packet[0] = 0x80;
            packet[1] = (byte)(isLast ? (DynamicPayloadType | 0x80) : DynamicPayloadType);
            WriteUInt16BE(packet, 2, _sequenceNumber++);
            WriteUInt32BE(packet, 4, timestamp);
            WriteUInt32BE(packet, 8, _ssrc);

            // FU indicator
            packet[RtpHeaderSize] = fuIndicator;

            // FU header: S=1 for first, E=1 for last, rest=0
            byte fuHeader = nalType;
            if (isFirst) fuHeader |= 0x80; // Start bit
            if (isLast) fuHeader |= 0x40;  // End bit
            packet[RtpHeaderSize + 1] = fuHeader;

            Buffer.BlockCopy(nal, offset, packet, RtpHeaderSize + FuHeaderSize, payloadSize);

            packets.Add(packet);
            offset += payloadSize;
            isFirst = false;

            if (isLast) break;
        }

        return packets;
    }

    private static void WriteUInt16BE(byte[] buf, int offset, ushort val)
    {
        buf[offset] = (byte)(val >> 8);
        buf[offset + 1] = (byte)val;
    }

    private static void WriteUInt32BE(byte[] buf, int offset, uint val)
    {
        buf[offset] = (byte)(val >> 24);
        buf[offset + 1] = (byte)(val >> 16);
        buf[offset + 2] = (byte)(val >> 8);
        buf[offset + 3] = (byte)val;
    }
}
