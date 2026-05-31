namespace UniLinker.Plugin.Sdk;

public record EncodedPacket(
    byte[] Data,
    long TimestampUs,
    bool IsKeyFrame);
