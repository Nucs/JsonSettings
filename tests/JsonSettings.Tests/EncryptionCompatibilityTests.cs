using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Pins the on-disk encrypted format against a ciphertext captured from an earlier build.
    /// </summary>
    /// <remarks>
    ///     The encryption path is a file format, not an implementation detail: a user's settings
    ///     file outlives any given version of this library, and there is no version marker inside
    ///     the ciphertext to migrate from. Anything that changes the cipher, the block mode, the
    ///     IV layout, the key size, the PBKDF2 iteration count or -- most easily overlooked -- the
    ///     PBKDF2 pseudorandom function silently renders every existing file undecryptable. The
    ///     symptom is a padding exception at load time with nothing pointing at the cause.
    ///
    ///     That is not hypothetical. Rfc2898DeriveBytes' three-argument constructor defaults to
    ///     SHA-1, and every file this library has ever written depends on that default; the
    ///     obvious "fix" for the SYSLIB0041 obsoletion is to move to a modern PRF, which would
    ///     have broken all of them. Hash.Pbkdf2 now passes HashAlgorithmName.SHA1 explicitly for
    ///     exactly this reason, and this test is what stops that from being undone.
    ///
    ///     The fixture below was produced by the pre-2.1.0 build (RijndaelManaged with an implicit
    ///     SHA-1 KDF) and must decrypt unchanged on every target. It matters that it runs on all
    ///     of them, because Hash.Pbkdf2 reaches the same derivation through three different APIs
    ///     depending on the framework: the static Rfc2898DeriveBytes.Pbkdf2 on net6.0+, the
    ///     HashAlgorithmName constructor overload on net48, and the obsolete constructor on
    ///     netstandard2.0, where neither of the others exists.
    /// </remarks>
    [TestClass]
    public class EncryptionCompatibilityTests {
        private const string Password = "SuperPassword";

        /// <summary>
        ///     A <see cref="CompatSettings"/> holding Value="round-trip me" and Number=42,
        ///     encrypted with <see cref="Password"/> by the pre-2.1.0 build. 80 bytes.
        /// </summary>
        /// <remarks>
        ///     A plain settings class rather than a SettingsBag, deliberately. A bag serialises
        ///     its backing dictionary with a Newtonsoft $type discriminator that names the
        ///     runtime's core assembly -- System.Private.CoreLib on .NET, mscorlib on .NET
        ///     Framework -- so a bag fixture captured on one runtime fails to DESERIALISE on the
        ///     other even when the decryption was perfectly correct. That would make this test
        ///     report a false crypto regression on net472/net48. (The underlying cross-runtime
        ///     limitation of encrypted SettingsBag files is real and pre-existing; it is simply
        ///     not what this test is here to measure.)
        /// </remarks>
        private const string PreExistingCiphertextBase64 =
            "Ic7gehKbtG8To0EDPbc+BN0NwL1zxPZ6X9UTc8GO7j0WPEXrk7BJ2cnl35FtOT15LWnBucRwi3iGrUQWnDNfea0mGLXq" +
            "7t0v6V9DsPNCf3o=";

        [TestMethod]
        public void DecryptsCiphertextWrittenByAnEarlierVersion() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Convert.FromBase64String(PreExistingCiphertextBase64));

            var settings = JsonSettings.Configure<CompatSettings>(f.FileName)
                                       .WithEncryption(Password)
                                       .LoadNow();

            settings.Value.Should().Be("round-trip me");
            settings.Number.Should().Be(42);
        }

        [TestMethod]
        public void RoundTripsFreshlyWrittenCiphertext() {
            using var f = new TempFile();

            var written = JsonSettings.Configure<CompatSettings>(f.FileName)
                                      .WithEncryption(Password)
                                      .LoadNow();
            written.Value = "round-trip me";
            written.Number = 42;
            written.Save();

            var read = JsonSettings.Configure<CompatSettings>(f.FileName)
                                   .WithEncryption(Password)
                                   .LoadNow();

            read.Value.Should().Be("round-trip me");
            read.Number.Should().Be(42);
        }

        [TestMethod]
        public void RejectsCiphertextTruncatedInsideTheInitializationVector() {
            using var f = new TempFile();
            var full = Convert.FromBase64String(PreExistingCiphertextBase64);

            //cut inside the 16-byte IV. This used to be read with the return value discarded,
            //which left the tail of the IV as zeros and decrypted to garbage instead of failing.
            var truncated = new byte[10];
            Array.Copy(full, truncated, truncated.Length);
            File.WriteAllBytes(f.FileName, truncated);

            new Action(() => JsonSettings.Configure<CompatSettings>(f.FileName)
                                         .WithEncryption(Password)
                                         .LoadNow())
                .Should().Throw<Exception>("a file too short to hold an IV cannot be decrypted");
        }

        public class CompatSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public int Number { get; set; }
        }
    }
}
