using System;
using System.IO;
using System.Security.Cryptography;

namespace XProtocolLib
{
    public class XProtocolEncryptor
    {
        private static readonly string Key = "2e985f930853919313c96d001cb5701f"; 

        public static byte[] Encrypt(byte[] data)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Convert.FromBase64String(Key);
                aes.IV = new byte[16]; 

                using (var encryptor = aes.CreateEncryptor())
                {
                    return PerformCryptography(data, encryptor);
                }
            }
        }

        public static byte[] Decrypt(byte[] data)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Convert.FromBase64String(Key);
                aes.IV = new byte[16];

                using (var decryptor = aes.CreateDecryptor())
                {
                    return PerformCryptography(data, decryptor);
                }
            }
        }

        private static byte[] PerformCryptography(byte[] data, ICryptoTransform cryptoTransform)
        {
            using (var ms = new MemoryStream())
            using (var cryptoStream = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Write))
            {
                cryptoStream.Write(data, 0, data.Length);
                cryptoStream.FlushFinalBlock();
                return ms.ToArray();
            }
        }

    }
}
