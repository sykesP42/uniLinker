namespace UniLinker.Core;

/// <summary>
/// RFC 6184 H.264 RTP depacketizer.
/// Reassembles FU-A fragments into complete NAL units.
/// </summary>
public class RtpDepacketizer
{
    private readonly List<byte[]> _pendingFuA = [];
    private bool _fuAInProgress;
    private byte _fuANri;
    private byte _fuAType;

    /// <summary>
    /// Process a raw RTP packet. Returns a complete NAL unit when a frame is
    /// fully reassembled, or null if more fragments are needed.
    /// </summary>
    public byte[]? ProcessPacket(byte[] rtpPacket)
    {
        if (rtpPacket.Length < 14) return null;

        // RTP header is 12 bytes
        int payloadOffset = 12;
        byte nalHeader = rtpPacket[payloadOffset];
        byte nalType = (byte)(nalHeader & 0x1F);

        if (nalType <= 23)
        {
            // Single NAL unit packet — return as-is
            int payloadSize = rtpPacket.Length - payloadOffset;
            var nal = new byte[payloadSize];
            Buffer.BlockCopy(rtpPacket, payloadOffset, nal, 0, payloadSize);
            return nal;
        }
        else if (nalType == 28) // FU-A
        {
            byte fuHeader = rtpPacket[payloadOffset + 1];
            bool start = (fuHeader & 0x80) != 0;
            bool end = (fuHeader & 0x40) != 0;
            byte fragmentNalType = (byte)(fuHeader & 0x1F);
            byte nri = (byte)(nalHeader & 0x60);

            if (start)
            {
                _pendingFuA.Clear();
                _fuAInProgress = true;
                _fuANri = nri;
                _fuAType = fragmentNalType;
            }

            if (_fuAInProgress)
            {
                int fragmentSize = rtpPacket.Length - payloadOffset - 2;
                var fragment = new byte[fragmentSize];
                Buffer.BlockCopy(rtpPacket, payloadOffset + 2, fragment, 0, fragmentSize);
                _pendingFuA.Add(fragment);
            }

            if (end && _fuAInProgress)
            {
                _fuAInProgress = false;

                // Reconstruct NAL: 1-byte header + all fragments
                int totalSize = 1 + _pendingFuA.Sum(f => f.Length);
                var nal = new byte[totalSize];
                nal[0] = (byte)(_fuANri | _fuAType);
                int offset = 1;
                foreach (var frag in _pendingFuA)
                {
                    Buffer.BlockCopy(frag, 0, nal, offset, frag.Length);
                    offset += frag.Length;
                }
                _pendingFuA.Clear();
                return nal;
            }
        }

        return null;
    }

    /// <summary>Reset state for a new stream.</summary>
    public void Reset()
    {
        _pendingFuA.Clear();
        _fuAInProgress = false;
    }
}
