using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XProtocolLib
{
    public class XPacket
    {
        private static readonly byte[] HEADER = { 0xAF, 0xAA, 0xAF };
        private static readonly byte[] FOOTER = { 0xFF, 0x00 };

        public byte PacketType { get; private set; }
        public byte PacketSubtype { get; private set; }
        public List<XPacketField> Fields { get; set; } = new List<XPacketField>();
        public bool Protected { get; private set; }

        private XPacket(byte type, byte subtype)
        {
            PacketType = type;
            PacketSubtype = subtype;
        }

        public static XPacket Create(byte type, byte subtype)
        {
            return new XPacket(type, subtype);
        }

        // Преобразуем объект в массив байтов для передачи по сети
        public byte[] ToPacket()
        {
            using (var packet = new MemoryStream())
            {
                packet.Write(HEADER, 0, HEADER.Length);
                packet.WriteByte(PacketType);
                packet.WriteByte(PacketSubtype);

                foreach (var field in Fields.OrderBy(f => f.FieldID))
                {
                    packet.WriteByte(field.FieldID);
                    packet.WriteByte(field.FieldSize);
                    packet.Write(field.Contents, 0, field.Contents.Length);
                }

                packet.Write(FOOTER, 0, FOOTER.Length);
                return packet.ToArray();
            }
        }

        // Преобразуем массив байтов обратно в объект (Есть поддержка шифрования)
        public static XPacket Parse(byte[] packet, bool markAsEncrypted = false)
        {
            if (packet.Length < 7)
                return null;

            bool encrypted = false;

            if (packet[0] != 0xAF || packet[1] != 0xAA || packet[2] != 0xAF)
            {
                if (packet[0] == 0x95 && packet[1] == 0xAA && packet[2] == 0xFF)
                {
                    encrypted = true;
                }
                else
                {
                    return null;
                }
            }

            var xpacket = new XPacket(packet[3], packet[4]) { Protected = encrypted };

            var fields = packet.Skip(5).Take(packet.Length - 7).ToArray();

            while (fields.Length > 2)
            {
                byte id = fields[0];
                byte size = fields[1];
                byte[] content = fields.Skip(2).Take(size).ToArray();
                xpacket.Fields.Add(new XPacketField { FieldID = id, FieldSize = size, Contents = content });
                fields = fields.Skip(2 + size).ToArray();
            }

            return encrypted ? DecryptPacket(xpacket) : xpacket;
        }


        // Преобразует объект (int, float) в массив байтов
        private static byte[] FixedObjectToByteArray(object value)
        {
            int rawsize = Marshal.SizeOf(value);
            byte[] rawdata = new byte[rawsize];

            GCHandle handle = GCHandle.Alloc(rawdata, GCHandleType.Pinned);
            Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
            handle.Free();

            return rawdata;
        }

        // Преобразует массив байтов обратно в объект нужного типа
        private static T ByteArrayToFixedObject<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            T structure = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();
            return structure;
        }

        // Упрощенная запись данных в пакет
        public void SetValue<T>(byte id, T value) where T : struct
        {
            byte[] bytes = FixedObjectToByteArray(value);
            if (bytes.Length > byte.MaxValue) throw new Exception("Слишком большой объект!");

            XPacketField field = new XPacketField { FieldID = id, FieldSize = (byte)bytes.Length, Contents = bytes };
            Fields.Add(field);
        }

        // Универсальная версия SetValue для вызова через рефлексию
        public void SetValueDynamic(byte id, object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            var method = typeof(XPacket)
                .GetMethod(nameof(SetValue))
                .MakeGenericMethod(value.GetType());

            method.Invoke(this, new object[] { id, value });
        }


        // Записываем byte[] напрямую в пакет
        public void SetValueRaw(byte id, byte[] rawData)
        {
            var field = GetField(id);

            if (field == null)
            {
                field = new XPacketField
                {
                    FieldID = id
                };
                Fields.Add(field);
            }

            if (rawData.Length > byte.MaxValue)
            {
                throw new Exception("Object is too big.");
            }

            field.FieldSize = (byte)rawData.Length;
            field.Contents = rawData;
        }


        // Упрощенное чтение данных из пакета
        public T GetValue<T>(byte id) where T : struct
        {
            XPacketField field = Fields.Find(f => f.FieldID == id);
            if (field == null) throw new Exception($"Поле {id} не найдено!");

            return ByteArrayToFixedObject<T>(field.Contents);
        }

        // Универсальный метод GetValue для объектов
        public object GetValue(Type type, byte id)
        {
            var method = typeof(XPacket)
                .GetMethod(nameof(GetValue))
                .MakeGenericMethod(type);

            return method.Invoke(this, new object[] { id });
        }


        // Получаем byte[] из пакета
        public byte[] GetValueRaw(byte id)
        {
            var field = GetField(id);

            if (field == null)
            {
                throw new Exception($"Field with ID {id} wasn't found.");
            }

            return field.Contents;
        }


        // Проверяет, есть ли поле с данным ID
        public bool HasField(byte id)
        {
            return Fields.Any(f => f.FieldID == id);
        }

        // Возвращает поле по ID (или null, если не найдено)
        public XPacketField GetField(byte id)
        {
            return Fields.Find(f => f.FieldID == id);
        }

        
        // Создаем атрибут для полей объекта, который будем конвертить, заодно указываем Id
        [AttributeUsage(AttributeTargets.Field)]
        public class XFieldAttribute : Attribute
        {
            public byte FieldID { get; }
            public XFieldAttribute(byte fieldId) => FieldID = fieldId;
        }

        
        //Сериализуем объект T в XPacket
        public static XPacket Serialize<T>(byte type, byte subtype, T obj)
        {
            var packet = Create(type, subtype);
            var fields = typeof(T).GetFields();

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<XFieldAttribute>();
                if (attr != null)
                {
                    object value = field.GetValue(obj);
                    if (value != null)
                    {
                        packet.SetValueDynamic(attr.FieldID, value);
                    }
                }
            }

            return packet;
        }

        //Десериализуем XPacket в объект T
        public static T Deserialize<T>(XPacket packet) where T : new()
        {
            var obj = new T();
            var fields = typeof(T).GetFields();

            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<XFieldAttribute>();
                if (attr != null && packet.HasField(attr.FieldID))
                {
                    object value = packet.GetValue(field.FieldType, attr.FieldID);
                    field.SetValue(obj, value);
                }
            }

            return obj;
        }


        // Шифруем пакет
        public static XPacket EncryptPacket(XPacket packet)
        {
            if (packet == null)
                return null;

            var rawBytes = packet.ToPacket();
            var encrypted = XProtocolEncryptor.Encrypt(rawBytes);

            var p = Create(0, 0);
            p.SetValueRaw(0, encrypted);
            p.Protected = true;

            return p;
        }
        

        // Расшифровываем пакет
        public static XPacket DecryptPacket(XPacket packet)
        {
            if (!packet.HasField(0))
            {
                return null;
            }

            var rawData = packet.GetValueRaw(0);
            var decrypted = XProtocolEncryptor.Decrypt(rawData);

            return Parse(decrypted, true);
        }

        
        public XPacket Encrypt()
        {
            return EncryptPacket(this);
        }

        public XPacket Decrypt()
        {
            return DecryptPacket(this);
        }
    }
}
