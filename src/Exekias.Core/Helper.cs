using System.IO;
using System.Security.Cryptography;
using System.Text;
using System;

namespace Exekias.Core
{
    public static class Utils
    {
        public static string ComputeSHA256(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            return BytesToHex(hash);
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.AppendFormat("{0:x2}", b);
            return sb.ToString().ToUpperInvariant();
        }
    }
}