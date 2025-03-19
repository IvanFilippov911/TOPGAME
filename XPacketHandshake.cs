
using XProtocolLib;

namespace Assets.Scripts.XPacketTypes
{
    public class XPacketHandshake
    {
        [XPacket.XField(1)]
        public int MagicHandshakeNumber;
    }
}
