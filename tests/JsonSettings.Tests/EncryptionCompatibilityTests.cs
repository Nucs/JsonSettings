using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

        /// <summary>
        ///     A wrong password whose decryption happens to survive padding validation must still
        ///     be reported as a wrong password.
        /// </summary>
        /// <remarks>
        ///     Detection used to rest entirely on the CryptographicException raised by PKCS7
        ///     padding validation, which is a probabilistic signal: CBC decryption under the
        ///     wrong key yields random bytes whose final block is valid padding by chance about
        ///     once in 256 attempts - measured at 0.40% over 3000 loads. In those cases
        ///     decryption "succeeded", garbage reached the JSON parser, and the caller was told
        ///     "Unable to parse file" with a JsonReaderException about an unexpected character.
        ///     That reads as a corrupt file rather than a mistyped password, which is the one
        ///     thing the message needed to convey.
        ///
        ///     Not hypothetical: it surfaced as a red CI run, on one framework out of five, in
        ///     SettingsBag_InvalidPassword.
        ///
        ///     "p433" is not arbitrary. It was found by searching wrong passwords against the
        ///     fixture above for one whose decryption passes padding validation, so this hits the
        ///     0.4% branch on EVERY run instead of once in 256. Both AES and PBKDF2 are
        ///     deterministic functions of key and input, so the collision reproduces identically
        ///     on every framework and platform - including across the three different KDF APIs
        ///     Hash.Pbkdf2 uses. Verified failing against the unfixed library, where it reports
        ///     the parse error quoted above.
        /// </remarks>
        [TestMethod]
        public void ReportsAWrongPasswordAsAWrongPasswordEvenWhenPaddingValidates() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Convert.FromBase64String(PreExistingCiphertextBase64));

            new Action(() => JsonSettings.Configure<CompatSettings>(f.FileName)
                                         .WithEncryption("p433")
                                         .LoadNow())
                .Should().Throw<JsonSettingsException>()
                .Where(e => e.Message.StartsWith("Password", StringComparison.OrdinalIgnoreCase),
                       "a wrong password must blame the password, not the file contents");
        }

        public class CompatSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public int Number { get; set; }
        }
    }
}
