
using XProtocolLib;

namespace Assets.Scripts.XPacketTypes
{
    public class PlayerAttack
    {
        [XPacket.XField(1)]
        public int PlayerId;

        [XPacket.XField(2)]
        public float AttackX;

        [XPacket.XField(3)]
        public float AttackY;

        [XPacket.XField(4)]
        public float Damage;

        [XPacket.XField(5)]
        public bool IsRight;

        [XPacket.XField(6)]
        public float Distance;
    }
}
