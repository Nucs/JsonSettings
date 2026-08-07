using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Encryption;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Parity between the migrated encryption (EncryptionModule on System.Security.Cryptography) and
    ///     the exact third-party Rijndael256 implementation the library shipped before the migration.
    /// </summary>
    /// <remarks>
    ///     The pre-migration sources live under <c>Legacy/Rijndael256/</c> in this test project - moved
    ///     out of the product, kept here as a compatibility oracle. Rather than pinning one captured
    ///     ciphertext, these tests run the actual old code beside the new code and assert they are
    ///     interchangeable: the key derivation is byte-for-byte identical, and a payload encrypted by
    ///     one decrypts with the other, in both directions, for the default AES-256-CBC path, every key
    ///     size, and the text / binary-password / raw-key credential forms.
    ///
    ///     Only the code the shipped module actually used is carried over (Rijndael, Hash, Rng,
    ///     Rijndael256Settings, KeySize). The old RijndaelEtM/AeKeyRing Encrypt-then-MAC classes were
    ///     never wired into RijndaelModule - dead code - so there is nothing of theirs to be compatible
    ///     with.
    ///
    ///     Reached through friend access (InternalsVisibleTo): the current KeyDerivation and AesCbcEngine
    ///     are internal, and comparing them directly to the legacy methods is the point.
    /// </remarks>
    [TestClass]
    public class LegacyEncryptionParityTests {
        private static readonly string[] Passwords = {
            "", "a", "SuperPassword", "p433", "correct horse battery staple",
            "pä$$wörd with unicode 🔐 and spaces", new string('x', 200)
        };

        private static byte[] Bytes(int length, int seed = 1) {
            var b = new byte[length];
            for (var i = 0; i < length; i++)
                b[i] = (byte) (i * seed + 13);
            return b;
        }

        private static byte[] RandomBytes(int size) {
            var b = new byte[size];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(b);
            return b;
        }

        // ------------------------------------------------------------ key derivation is byte-identical

        [DataTestMethod]
        [DataRow(128, 16)]
        [DataRow(192, 24)]
        [DataRow(256, 32)]
        public void TextKeyDerivation_IsByteIdenticalToLegacy(int bits, int keyBytes) {
            foreach (var pw in Passwords) {
                var legacy = Rijndael256.Rijndael.GenerateKey(pw, (Rijndael256.KeySize) bits);
                var current = KeyDerivation.FromText(pw, keyBytes);
                current.Should().Equal(legacy, $"text KDF must equal legacy for password '{pw}' at {bits}-bit");
            }
        }

        [DataTestMethod]
        [DataRow(128, 16)]
        [DataRow(192, 24)]
        [DataRow(256, 32)]
        public void ByteKeyDerivation_IsByteIdenticalToLegacy(int bits, int keyBytes) {
            var samples = new[] {
                Array.Empty<byte>(), new byte[] { 0 }, new byte[] { 0, 1, 0xFF, 0xFE, 0x80, 0x7F, 0x2A },
                Bytes(20, 3), RandomBytes(64)
            };
            foreach (var pw in samples) {
                var legacy = Rijndael256.Rijndael.GenerateKey(pw, (Rijndael256.KeySize) bits);
                var current = KeyDerivation.FromBytes(pw, keyBytes);
                current.Should().Equal(legacy, $"binary KDF must equal legacy for {pw.Length} bytes at {bits}-bit");
            }
        }

        // ------------------------------------------------------------ cipher round-trips across old/new

        [DataTestMethod]
        [DataRow(128, 16)]
        [DataRow(192, 24)]
        [DataRow(256, 32)]
        public void AesCbcCipher_IsInterchangeableWithLegacy_BothDirections(int bits, int keyBytes) {
            var data = Encoding.UTF8.GetBytes("{\"Value\":\"cipher parity\",\"Number\":7}");
            var key = RandomBytes(keyBytes);
            var engine = new AesCbcEngine((KeySize) bits);

            // current encrypts -> legacy decrypts (verbatim key overload)
            var currentCipher = engine.Encrypt(data, key);
            Rijndael256.Rijndael.DecryptBytes(currentCipher, key).Should().Equal(data,
                "a file the current AES-CBC engine wrote must decrypt with the legacy method");

            // legacy encrypts -> current decrypts
            var legacyCipher = Rijndael256.Rijndael.Encrypt(data, key, RandomBytes(16));
            engine.Decrypt(legacyCipher, key).Should().Equal(data,
                "a file the legacy method wrote must decrypt with the current AES-CBC engine");

            // same on-disk shape: IV(16) + PKCS7 blocks
            currentCipher.Length.Should().Be(legacyCipher.Length);
            (currentCipher.Length % 16).Should().Be(0);
        }

        // ------------------------------------------------------------ the default, end to end, both ways

        [TestMethod]
        public void Default_IsAes256Cbc_WithTheSameKeyAsLegacy() {
            var module = new EncryptionModule("pw");
            module.Algorithm.Should().Be(EncryptionAlgorithm.AesCbc);
            module.KeySize.Should().Be(KeySize.Aes256);

            KeyDerivation.FromText("pw", 32)
                .Should().Equal(Rijndael256.Rijndael.GenerateKey("pw", Rijndael256.KeySize.Aes256),
                    "the module's default derives the same key legacy did for AES-256");
        }

        [TestMethod]
        public void DefaultEncryption_FileWrittenByLegacy_ReadsWithCurrentModule() {
            foreach (var pw in Passwords) {
                var json = "{\"Value\":\"legacy wrote me\",\"Number\":123}";
                var fileBytes = Rijndael256.Rijndael.Encrypt(Encoding.UTF8.GetBytes(json), pw, RandomBytes(16), Rijndael256.KeySize.Aes256);

                using var f = new TempFile();
                File.WriteAllBytes(f.FileName, fileBytes);

                var read = JsonSettings.Configure<ParitySettings>(f.FileName).WithEncryption(pw).LoadNow();
                read.Value.Should().Be("legacy wrote me", $"legacy-written default file must load, password '{pw}'");
                read.Number.Should().Be(123);
            }
        }

        [TestMethod]
        public void DefaultEncryption_FileWrittenByCurrentModule_ReadsWithLegacyMethods() {
            foreach (var pw in Passwords) {
                using var f = new TempFile();
                var written = JsonSettings.Configure<ParitySettings>(f.FileName).WithEncryption(pw).LoadNow();
                written.Value = "current wrote me";
                written.Number = 321;
                written.Save();

                var plaintext = Rijndael256.Rijndael.DecryptBytes(File.ReadAllBytes(f.FileName), pw, Rijndael256.KeySize.Aes256);
                var json = Encoding.UTF8.GetString(plaintext);
                json.Should().Contain("current wrote me").And.Contain("321",
                    $"the legacy method must decrypt a current-written default file, password '{pw}'");
            }
        }

        // ------------------------------------------------------------ raw key and binary password, end to end

        [TestMethod]
        public void RawKey_IsInterchangeableWithLegacy_BothDirections() {
            var key = RandomBytes(32);
            var json = "{\"Value\":\"raw parity\",\"Number\":9}";

            // legacy raw-key encrypt -> current raw-key read
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Rijndael256.Rijndael.Encrypt(Encoding.UTF8.GetBytes(json), key, RandomBytes(16)));
            JsonSettings.Configure<ParitySettings>(f.FileName).WithEncryptionRawKey(key).LoadNow()
                .Value.Should().Be("raw parity");

            // current raw-key encrypt -> legacy raw-key decrypt
            using var f2 = new TempFile();
            var w = JsonSettings.Configure<ParitySettings>(f2.FileName).WithEncryptionRawKey(key).LoadNow();
            w.Value = "raw current";
            w.Save();
            Encoding.UTF8.GetString(Rijndael256.Rijndael.DecryptBytes(File.ReadAllBytes(f2.FileName), key))
                .Should().Contain("raw current");
        }

        [TestMethod]
        public void BinaryPassword_FileWrittenByCurrentModule_ReadsViaLegacyDerivedKey() {
            var bytePw = new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x80, 0x7F, 0x2A };

            using var f = new TempFile();
            var w = JsonSettings.Configure<ParitySettings>(f.FileName).WithEncryption(bytePw).LoadNow();
            w.Value = "binary parity";
            w.Save();

            // Legacy has no byte[]-password decrypt overload; derive the key its way, then decrypt verbatim.
            var legacyKey = Rijndael256.Rijndael.GenerateKey(bytePw, Rijndael256.KeySize.Aes256);
            Encoding.UTF8.GetString(Rijndael256.Rijndael.DecryptBytes(File.ReadAllBytes(f.FileName), legacyKey))
                .Should().Contain("binary parity");
        }

        public class ParitySettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public int Number { get; set; }
        }
    }
}
