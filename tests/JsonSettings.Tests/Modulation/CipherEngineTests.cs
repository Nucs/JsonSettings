using System;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation.Encryption;

namespace Nucs.JsonSettings.Tests.Modulation {
    /// <summary>
    ///     Direct unit tests for the internal <see cref="CipherEngine"/> implementations behind
    ///     <see cref="EncryptionModule"/>. The module round-trip tests reach the happy path, but the
    ///     engines' own guards -- raw-key length validation, the too-short-ciphertext rejection, tamper
    ///     detection and <see cref="CipherEngineFactory"/> dispatch -- are exercised here where they can
    ///     be provoked deterministically.
    /// </summary>
    [TestClass]
    public class CipherEngineTests {
        private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("{\"hello\":\"world\",\"n\":42}");

        private static byte[] Key(int bytes) {
            var k = new byte[bytes];
            RandomNumberGenerator.Create().GetBytes(k);
            return k;
        }

        private static void RoundTrips(CipherEngine engine) {
            var key = Key(engine.KeyBytes);
            var ct = engine.Encrypt(Plaintext, key);
            ct.Length.Should().BeGreaterThanOrEqualTo(engine.MinimumLength);
            engine.Decrypt(ct, key).Should().Equal(Plaintext);
        }

        [TestMethod]
        public void AesCbc_RoundTrips_AndReportsUnauthenticated() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesCbc, KeySize.Aes256);
            engine.Should().BeOfType<AesCbcEngine>();
            engine.KeyBytes.Should().Be(32);
            engine.MinimumLength.Should().Be(16);
            engine.IsAuthenticated.Should().BeFalse();
            RoundTrips(engine);
        }

        [TestMethod]
        public void AesCbc_KeyBytes_TrackKeySize() {
            CipherEngineFactory.Create(EncryptionAlgorithm.AesCbc, KeySize.Aes128).KeyBytes.Should().Be(16);
            CipherEngineFactory.Create(EncryptionAlgorithm.AesCbc, KeySize.Aes192).KeyBytes.Should().Be(24);
            CipherEngineFactory.Create(EncryptionAlgorithm.AesCbc, KeySize.Aes256).KeyBytes.Should().Be(32);
        }

        [TestMethod]
        public void AesCbcHmac_RoundTrips_AndIsAuthenticated() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesCbcHmac, KeySize.Aes256);
            engine.Should().BeOfType<AesCbcHmacEngine>();
            engine.KeyBytes.Should().Be(32 + 32);
            engine.MinimumLength.Should().Be(16 + 32);
            engine.IsAuthenticated.Should().BeTrue();
            RoundTrips(engine);
        }

        [TestMethod]
        public void AesCbcHmac_ValidateRawKey_AcceptsCipherPlusMacLengths() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesCbcHmac, KeySize.Aes256);
            //16/24/32-byte AES key followed by the 32-byte MAC key = 48/56/64 total.
            new Action(() => engine.ValidateRawKey(Key(48))).Should().NotThrow();
            new Action(() => engine.ValidateRawKey(Key(56))).Should().NotThrow();
            new Action(() => engine.ValidateRawKey(Key(64))).Should().NotThrow();
        }

        [TestMethod]
        public void AesCbcHmac_ValidateRawKey_RejectsWrongLengthAndNull() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesCbcHmac, KeySize.Aes256);
            new Action(() => engine.ValidateRawKey(Key(32))).Should().Throw<ArgumentException>();
            new Action(() => engine.ValidateRawKey(null!)).Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void AesCbcHmac_TamperedMac_ThrowsCryptographic() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesCbcHmac, KeySize.Aes256);
            var key = Key(engine.KeyBytes);
            var ct = engine.Encrypt(Plaintext, key);
            ct[ct.Length - 1] ^= 0xFF; //flip a bit in the trailing HMAC

            new Action(() => engine.Decrypt(ct, key)).Should().Throw<CryptographicException>();
        }

        [TestMethod]
        public void AesCbcHmac_TooShortCiphertext_ThrowsCryptographic() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesCbcHmac, KeySize.Aes256);
            var key = Key(engine.KeyBytes);
            //Shorter than IV(16)+MAC(32): the engine's own length guard fires.
            new Action(() => engine.Decrypt(new byte[10], key)).Should().Throw<CryptographicException>();
        }

        [TestMethod]
        public void Factory_UnknownAlgorithm_Throws() {
            new Action(() => CipherEngineFactory.Create((EncryptionAlgorithm) 999, KeySize.Aes256))
                .Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestMethod]
        public void Factory_IsAuthenticated_OnlyAesCbcIsNot() {
            CipherEngineFactory.IsAuthenticated(EncryptionAlgorithm.AesCbc).Should().BeFalse();
            CipherEngineFactory.IsAuthenticated(EncryptionAlgorithm.AesCbcHmac).Should().BeTrue();
        }

        [TestMethod]
        public void AesKeyLengths_Validate_AcceptsAesLengths_RejectsOthers() {
            new Action(() => AesKeyLengths.Validate(Key(16))).Should().NotThrow();
            new Action(() => AesKeyLengths.Validate(Key(24))).Should().NotThrow();
            new Action(() => AesKeyLengths.Validate(Key(32))).Should().NotThrow();
            new Action(() => AesKeyLengths.Validate(Key(15))).Should().Throw<ArgumentException>();
            new Action(() => AesKeyLengths.Validate(null!)).Should().Throw<ArgumentNullException>();
        }

#if NET6_0_OR_GREATER
        [TestMethod]
        public void AesGcm_RoundTrips_AndTamperFails() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesGcm, KeySize.Aes256);
            engine.Should().BeOfType<AesGcmEngine>();
            engine.KeyBytes.Should().Be(32);
            engine.MinimumLength.Should().Be(12 + 16);
            engine.IsAuthenticated.Should().BeTrue();
            RoundTrips(engine);

            var key = Key(engine.KeyBytes);
            var ct = engine.Encrypt(Plaintext, key);
            ct[ct.Length - 1] ^= 0xFF; //corrupt the tag
            new Action(() => engine.Decrypt(ct, key)).Should().Throw<CryptographicException>();
        }

        [TestMethod]
        public void AesCcm_RoundTrips() {
            if (!System.Security.Cryptography.AesCcm.IsSupported)
                Assert.Inconclusive("AES-CCM is not supported on this OS.");

            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesCcm, KeySize.Aes256);
            engine.Should().BeOfType<AesCcmEngine>();
            RoundTrips(engine);
        }

        [TestMethod]
        public void ChaCha20Poly1305_RoundTrips_AndUsesFixed256BitKey() {
            if (!ChaCha20Poly1305.IsSupported)
                Assert.Inconclusive("ChaCha20-Poly1305 is not supported on this OS.");

            //ChaCha ignores KeySize; a 128-bit request still resolves to a 32-byte key.
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.ChaCha20Poly1305, KeySize.Aes128);
            engine.Should().BeOfType<ChaCha20Poly1305Engine>();
            engine.KeyBytes.Should().Be(32);
            RoundTrips(engine);

            new Action(() => engine.ValidateRawKey(Key(16))).Should().Throw<ArgumentException>();
            new Action(() => engine.ValidateRawKey(Key(32))).Should().NotThrow();
        }

        [TestMethod]
        public void Aead_TooShortCiphertext_ThrowsCryptographic() {
            var engine = CipherEngineFactory.Create(EncryptionAlgorithm.AesGcm, KeySize.Aes256);
            var key = Key(engine.KeyBytes);
            //Shorter than nonce(12)+tag(16).
            new Action(() => engine.Decrypt(new byte[10], key)).Should().Throw<CryptographicException>();
        }
#endif
    }
}
