using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Exekias.Core;

namespace Exekias.Core.Tests
{
    public class SHA256Tests
    {
        [Fact]
        public void ComputeSHA256_NullPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Utils.ComputeSHA256(null));
        }

        [Fact]
        public void ComputeSHA256_EmptyFile_ReturnsCorrectHash()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                string hash = Utils.ComputeSHA256(tempFile);
                // SHA256 of empty string
                string expected = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
                Assert.Equal(expected, hash);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void ComputeSHA256_SameContent_ProducesSameHash()
        {
            string tempFile1 = Path.GetTempFileName();
            string tempFile2 = Path.GetTempFileName();

            try
            {
                File.WriteAllText(tempFile1, "Hello World!");
                File.WriteAllText(tempFile2, "Hello World!");

                string hash1 = Utils.ComputeSHA256(tempFile1);
                string hash2 = Utils.ComputeSHA256(tempFile2);

                Assert.Equal(hash1, hash2);
            }
            finally
            {
                File.Delete(tempFile1);
                File.Delete(tempFile2);
            }
        }

        [Fact]
        public void ComputeSHA256_DifferentContent_ProducesDifferentHash()
        {
            string tempFile1 = Path.GetTempFileName();
            string tempFile2 = Path.GetTempFileName();

            try
            {
                File.WriteAllText(tempFile1, "Hello World!");
                File.WriteAllText(tempFile2, "Hello World!!"); // note extra !

                string hash1 = Utils.ComputeSHA256(tempFile1);
                string hash2 = Utils.ComputeSHA256(tempFile2);

                Assert.NotEqual(hash1, hash2);
            }
            finally
            {
                File.Delete(tempFile1);
                File.Delete(tempFile2);
            }
        }
    }
}
