using System.Security.Cryptography;

namespace Bloxstrap.Utility
{
    public static class MD5Hash
    {
        public static byte[] Compute(Stream stream)
        {
            using MD5 md5 = MD5.Create();
            return md5.ComputeHash(stream);
        }

        public static string FromBytes(byte[] data) => Stringify(MD5.HashData(data));

        public static string FromStream(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);

            return Stringify(Compute(stream));
        }

        public static string FromFile(string filename)
        {
            using FileStream stream = File.OpenRead(filename);

            return Stringify(Compute(stream));
        }

        public static string FromString(string str) => FromBytes(Encoding.UTF8.GetBytes(str));

        public static string Stringify(byte[] hash)
        {
            var chars = new char[hash.Length * 2];

            for (int i = 0; i < hash.Length; i++)
            {
                chars[i * 2] = HexDigits[hash[i] >> 4];
                chars[i * 2 + 1] = HexDigits[hash[i] & 0x0F];
            }

            return new string(chars);
        }

        private const string HexDigits = "0123456789abcdef";
    }
}
