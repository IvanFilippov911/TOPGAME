
using XProtocolLib;

namespace Assets.Scripts.XPacketTypes
{
    public class PlayerPosition
    {
        [XPacket.XField(1)]
        public int PlayerId;

        [XPacket.XField(2)]
        public float X;

        [XPacket.XField(3)]
        public float Y;

        [XPacket.XField(4)]
        public bool IsRight;

        [XPacket.XField(5)]
        public string PlayerName;
    }

}
