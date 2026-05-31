namespace UniLinker.Plugin.Sdk;

[Flags]
public enum DeviceRole
{
    None = 0,
    Sender = 1 << 0,
    Receiver = 1 << 1,
    Both = Sender | Receiver,
}
