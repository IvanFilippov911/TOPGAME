
using XProtocolLib;

namespace Assets.Scripts.XPacketTypes
{
    public class PlayerData
    {
        [XPacket.XField(1)]
        public string Name;
        [XPacket.XField(2)]
        public string Character;
    }
}
