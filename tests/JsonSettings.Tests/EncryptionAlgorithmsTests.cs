using System;
using System.IO;
using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Covers every <see cref="EncryptionAlgorithm"/> the current target framework exposes: that it
    ///     round-trips, reports a wrong password, and - for the authenticated ones - rejects a tampered
    ///     file outright rather than leaning on the AES-CBC UTF-8 heuristic. The AEAD algorithms only
    ///     exist on .NET 6.0+, so the list they are tested from is #if-gated to match the library.
    /// </summary>
    [TestClass]
    public class EncryptionAlgorithmsTests {
        private const string Password = "correct horse battery staple";

        /// <summary>Every algorithm available on this target framework.</summary>
        private static EncryptionAlgorithm[] Available() {
            return new[] {
                EncryptionAlgorithm.AesCbc,
                EncryptionAlgorithm.AesCbcHmac,
#if NET6_0_OR_GREATER
                EncryptionAlgorithm.AesGcm,
                EncryptionAlgorithm.AesCcm,
                EncryptionAlgorithm.ChaCha20Poly1305,
#endif
            };
        }

        /// <summary>
        ///     ChaCha20-Poly1305 depends on OS support and advertises it; AES-GCM/CCM are assumed present
        ///     on every platform this library's CI runs on. A skipped algorithm is reported, not passed
        ///     silently.
        /// </summary>
        private static bool IsSupported(EncryptionAlgorithm algorithm) {
#if NET6_0_OR_GREATER
            if (algorithm == EncryptionAlgorithm.ChaCha20Poly1305)
                return ChaCha20Poly1305.IsSupported;
#endif
            return true;
        }

        private static byte[] Key(int length, int seed = 1) {
            var k = new byte[length];
            for (var i = 0; i < length; i++)
                k[i] = (byte) (i * seed + 7);
            return k;
        }

        [TestMethod]
        public void AesCbc_IsTheDefaultAlgorithm() {
            new EncryptionModule("x").Algorithm.Should().Be(EncryptionAlgorithm.AesCbc,
                "the default must stay the historical, on-disk-compatible format");

            // A file written with the default and one written with an explicit AesCbc must interchange.
            using var f = new TempFile();
            var w = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password).LoadNow();
            w.Value = "default";
            w.Save();

            var r = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password, EncryptionAlgorithm.AesCbc).LoadNow();
            r.Value.Should().Be("default");
        }

        [TestMethod]
        public void EveryAlgorithm_RoundTripsWithAPassword() {
            foreach (var algorithm in Available()) {
                if (!IsSupported(algorithm)) {
                    Console.WriteLine($"skipped (unsupported on this OS): {algorithm}");
                    continue;
                }

                using var f = new TempFile();
                var w = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password, algorithm).LoadNow();
                w.Value = "payload for " + algorithm;
                w.Number = 3;
                w.Save();

                var r = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password, algorithm).LoadNow();
                r.Value.Should().Be("payload for " + algorithm, $"{algorithm} must round-trip a password");
                r.Number.Should().Be(3);
            }
        }

        [TestMethod]
        public void EveryAlgorithm_ReportsAWrongPassword() {
            foreach (var algorithm in Available()) {
                if (!IsSupported(algorithm))
                    continue;

                using var f = new TempFile();
                var w = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption("right one", algorithm).LoadNow();
                w.Value = "secret";
                w.Save();

                new Action(() => JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption("wrong one", algorithm).LoadNow())
                    .Should().Throw<JsonSettingsException>($"{algorithm} must report a wrong password");
            }
        }

        [TestMethod]
        public void AuthenticatedAlgorithms_RejectATamperedFile() {
            foreach (var algorithm in Available()) {
                if (algorithm == EncryptionAlgorithm.AesCbc)
                    continue; // unauthenticated by design; tamper detection is only best-effort there
                if (!IsSupported(algorithm))
                    continue;

                using var f = new TempFile();
                var w = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password, algorithm).LoadNow();
                w.Value = "authentic";
                w.Save();

                // Flip a bit in the trailing authentication tag.
                var bytes = File.ReadAllBytes(f.FileName);
                bytes[bytes.Length - 1] ^= 0xFF;
                File.WriteAllBytes(f.FileName, bytes);

                new Action(() => JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password, algorithm).LoadNow())
                    .Should().Throw<JsonSettingsException>($"{algorithm} authenticates and must reject a tampered file");
            }
        }

        [TestMethod]
        public void RawKey_RoundTrips_ForAesLengthAlgorithms() {
            // A 32-byte verbatim key is valid for AES-256 and for ChaCha20-Poly1305 alike.
            var algorithms = new[] {
                EncryptionAlgorithm.AesCbc,
#if NET6_0_OR_GREATER
                EncryptionAlgorithm.AesGcm,
                EncryptionAlgorithm.AesCcm,
                EncryptionAlgorithm.ChaCha20Poly1305,
#endif
            };

            foreach (var algorithm in algorithms) {
                if (!IsSupported(algorithm))
                    continue;

                using var f = new TempFile();
                var key = Key(32, 2);
                var w = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryptionRawKey(key, algorithm).LoadNow();
                w.Value = "raw " + algorithm;
                w.Save();

                var r = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryptionRawKey(key, algorithm).LoadNow();
                r.Value.Should().Be("raw " + algorithm, $"{algorithm} must round-trip a verbatim key");
            }
        }

        [DataTestMethod]
        [DataRow(KeySize.Aes128, DisplayName = "AES-128")]
        [DataRow(KeySize.Aes192, DisplayName = "AES-192")]
        [DataRow(KeySize.Aes256, DisplayName = "AES-256")]
        public void KeySize_Variants_RoundTrip(KeySize keySize) {
            using var f = new TempFile();
            var w = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password, EncryptionAlgorithm.AesCbc, keySize).LoadNow();
            w.Value = "keysize " + keySize;
            w.Save();

            var r = JsonSettings.Configure<AlgoSettings>(f.FileName).WithEncryption(Password, EncryptionAlgorithm.AesCbc, keySize).LoadNow();
            r.Value.Should().Be("keysize " + keySize);
        }

        public class AlgoSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public int Number { get; set; }
        }
    }
}
