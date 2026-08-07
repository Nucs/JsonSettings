using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Proves the encryption module's on-disk format IS the .NET base class library's own format:
    ///     an independent reimplementation that touches nothing but <see cref="System.Security.Cryptography"/>
    ///     (no library types, no vendored code) both reads what the module writes and writes what the
    ///     module reads. This is the acceptance test for the migration off the third-party Rijndael256
    ///     helper - if the module ever drifts from plain BCL AES, these fail.
    /// </summary>
    /// <remarks>
    ///     The <see cref="Bcl"/> helper below is deliberately a from-scratch implementation of the KDF
    ///     and the cipher using only public BCL APIs, spelled with the per-target-framework #if arms a
    ///     real consumer would need (the one-shot PBKDF2 is .NET 6+, so net472/net48 take the instance
    ///     path). It is not the library's own code shared over - that would prove nothing.
    /// </remarks>
    [TestClass]
    public class BuiltInCryptoCompatibilityTests {
        private const string Password = "SuperPassword";

        /// <summary>
        ///     The pre-2.1.0 ciphertext pinned by <see cref="EncryptionCompatibilityTests"/>, produced by
        ///     the original RijndaelManaged build. Value="round-trip me", Number=42.
        /// </summary>
        private const string LegacyCiphertextBase64 =
            "Ic7gehKbtG8To0EDPbc+BN0NwL1zxPZ6X9UTc8GO7j0WPEXrk7BJ2cnl35FtOT15LWnBucRwi3iGrUQWnDNfea0mGLXq" +
            "7t0v6V9DsPNCf3o=";

        // ---------------------------------------------------------------- default format == BCL AES-256-CBC

        [TestMethod]
        public void DefaultEncryption_ProducesAes256Cbc_ReadableByPlainBcl() {
            using var f = new TempFile();
            var written = JsonSettings.Configure<CryptoSettings>(f.FileName).WithEncryption(Password).LoadNow();
            written.Value = "hello from the module";
            written.Number = 5;
            written.Save();

            var plaintext = Bcl.AesCbcDecrypt(File.ReadAllBytes(f.FileName), Bcl.TextKey(Password, 32));
            Encoding.UTF8.GetString(plaintext).Should().Contain("hello from the module",
                "the default WithEncryption format is exactly AES-256-CBC over the JSON, keyed by the PBKDF2-SHA1 KDF");
        }

        [TestMethod]
        public void Module_Reads_CiphertextWrittenByPlainBcl() {
            using var f = new TempFile();

            // Capture the exact JSON the module serializes (so the plaintext is well-formed for the class)...
            var seed = JsonSettings.Configure<CryptoSettings>(f.FileName).WithEncryption(Password).LoadNow();
            seed.Value = "round-tripped through BCL";
            seed.Number = 11;
            seed.Save();
            var plaintext = Bcl.AesCbcDecrypt(File.ReadAllBytes(f.FileName), Bcl.TextKey(Password, 32));

            // ...then re-encrypt it with pure BCL under a fresh IV. The module must read that verbatim.
            File.WriteAllBytes(f.FileName, Bcl.AesCbcEncrypt(plaintext, Bcl.TextKey(Password, 32)));

            var read = JsonSettings.Configure<CryptoSettings>(f.FileName).WithEncryption(Password).LoadNow();
            read.Value.Should().Be("round-tripped through BCL");
            read.Number.Should().Be(11);
        }

        [TestMethod]
        public void LegacyRijndaelManagedFile_DecryptsUnderTheMigratedModule() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Convert.FromBase64String(LegacyCiphertextBase64));

            var settings = JsonSettings.Configure<CryptoSettings>(f.FileName).WithEncryption(Password).LoadNow();
            settings.Value.Should().Be("round-trip me");
            settings.Number.Should().Be(42);
        }

        [TestMethod]
        public void RawKey_IsPlainBclAesCbcWithThatKey() {
            using var f = new TempFile();
            var key = Bcl.RandomBytes(32);

            var written = JsonSettings.Configure<CryptoSettings>(f.FileName).WithEncryptionRawKey(key).LoadNow();
            written.Value = "verbatim key";
            written.Save();

            var plaintext = Bcl.AesCbcDecrypt(File.ReadAllBytes(f.FileName), key);
            Encoding.UTF8.GetString(plaintext).Should().Contain("verbatim key",
                "a raw key is used exactly as an AES key, with no derivation");
        }

        // ---------------------------------------------------------------- obsolete shim still interops

        [TestMethod]
        public void ObsoleteRijndaelModule_WritesWhatEncryptionModuleReads_AndViceVersa() {
#pragma warning disable CS0618 // RijndaelModule is the retained backward-compat alias under test here.
            using var f = new TempFile();

            var viaShim = JsonSettings.Configure<CryptoSettings>(f.FileName).WithModule(new RijndaelModule(Password)).LoadNow();
            viaShim.Value = "written via the obsolete shim";
            viaShim.Save();

            // The current, renamed entry point reads it back unchanged.
            var viaNew = JsonSettings.Configure<CryptoSettings>(f.FileName).WithEncryption(Password).LoadNow();
            viaNew.Value.Should().Be("written via the obsolete shim");

            // And the shim's raw-key factory still resolves to a working module.
            using var f2 = new TempFile();
            var key = Bcl.RandomBytes(32);
            var rk = JsonSettings.Configure<CryptoSettings>(f2.FileName).WithModule(RijndaelModule.FromRawKey(key)).LoadNow();
            rk.Value = "shim raw key";
            rk.Save();
            var rkBack = JsonSettings.Configure<CryptoSettings>(f2.FileName).WithEncryptionRawKey(key).LoadNow();
            rkBack.Value.Should().Be("shim raw key");
#pragma warning restore CS0618
        }

        /// <summary>Settings shape these tests round-trip. Shared with <see cref="EncryptionAlgorithmsTests"/>.</summary>
        public class CryptoSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public int Number { get; set; }
        }

        /// <summary>
        ///     A from-scratch AES-256-CBC + PBKDF2-SHA1 implementation over public BCL APIs only, used to
        ///     independently verify the module's format. Mirrors no library code.
        /// </summary>
        internal static class Bcl {
            private const int Iterations = 10000;
            private const int IvSize = 16;

            internal static byte[] TextKey(string password, int keyBytes) {
                var pwd = Encoding.UTF8.GetBytes(password);
                var saltSeed = Encoding.UTF8.GetBytes(Hex(Sha512(Encoding.UTF8.GetBytes(password + password.Length))));
                var salt = Pbkdf2(pwd, saltSeed, 64);
                return Pbkdf2(pwd, salt, keyBytes);
            }

            internal static byte[] AesCbcEncrypt(byte[] plaintext, byte[] key) {
                var iv = RandomBytes(IvSize);
                using var aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var encryptor = aes.CreateEncryptor(key, iv);
                using var ms = new MemoryStream();
                ms.Write(iv, 0, iv.Length);
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write)) {
                    cs.Write(plaintext, 0, plaintext.Length);
                    cs.FlushFinalBlock();
                }

                return ms.ToArray();
            }

            internal static byte[] AesCbcDecrypt(byte[] file, byte[] key) {
                var iv = new byte[IvSize];
                Buffer.BlockCopy(file, 0, iv, 0, IvSize);
                var body = new byte[file.Length - IvSize];
                Buffer.BlockCopy(file, IvSize, body, 0, body.Length);

                using var aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using var decryptor = aes.CreateDecryptor(key, iv);
                using var ms = new MemoryStream(body);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var output = new MemoryStream();
                cs.CopyTo(output);
                return output.ToArray();
            }

            internal static byte[] RandomBytes(int size) {
                var bytes = new byte[size];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(bytes);
                return bytes;
            }

            private static byte[] Sha512(byte[] data) {
                using var sha = SHA512.Create();
                return sha.ComputeHash(data);
            }

            private static string Hex(byte[] data) => BitConverter.ToString(data).Replace("-", "");

            private static byte[] Pbkdf2(byte[] data, byte[] salt, int size) {
#if NET6_0_OR_GREATER
                return Rfc2898DeriveBytes.Pbkdf2(data, salt, Iterations, HashAlgorithmName.SHA1, size);
#elif NET472_OR_GREATER
                using var kdf = new Rfc2898DeriveBytes(data, salt, Iterations, HashAlgorithmName.SHA1);
                return kdf.GetBytes(size);
#else
                using var kdf = new Rfc2898DeriveBytes(data, salt, Iterations);
                return kdf.GetBytes(size);
#endif
            }
        }
    }
}
