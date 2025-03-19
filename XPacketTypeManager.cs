using System;
using System.Collections.Generic;
using System.Linq;

namespace XProtocolLib
{
    public static class XPacketTypeManager
    {
        private static readonly Dictionary<XPacketType, Tuple<byte, byte>> TypeDictionary = new Dictionary<XPacketType, Tuple<byte, byte>>();


        public static void RegisterType(XPacketType type, byte btype, byte bsubtype)
        {
            if (TypeDictionary.ContainsKey(type))
                throw new Exception($"Тип {type} уже зарегистрирован!");

            TypeDictionary[type] = Tuple.Create(btype, bsubtype);
        }

        public static Tuple<byte, byte> GetType(XPacketType type)
        {
            return TypeDictionary.TryGetValue(type, out var tuple) ? tuple : throw new Exception($"Тип {type} не зарегистрирован!");
        }

        public static XPacketType GetTypeFromPacket(XPacket packet)
        {
            return TypeDictionary.FirstOrDefault(t => t.Value.Item1 == packet.PacketType && t.Value.Item2 == packet.PacketSubtype).Key;
        }
    }

    public enum XPacketType
    {
        Unknown,
        Handshake,
        PositionUpdate,
        Attack,
        PlayerData,
        Ready
    }
}
