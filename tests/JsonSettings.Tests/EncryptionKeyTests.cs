using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Covers the two binary credential forms added to <c>WithEncryption</c>:
    ///     <c>WithEncryption(byte[])</c> - a binary password, PBKDF2-stretched like a text password -
    ///     and <c>WithEncryptionRawKey(byte[])</c> - bytes used verbatim as the AES key.
    /// </summary>
    /// <remarks>
    ///     The text-password path and the on-disk format are asserted elsewhere
    ///     (<see cref="EncryptionCompatibilityTests"/>); these tests only cover the new entry points
    ///     and the invariants that separate them from each other and from the text path.
    /// </remarks>
    [TestClass]
    public class EncryptionKeyTests {
        private static byte[] Key(int length, int seed = 1) {
            var k = new byte[length];
            for (var i = 0; i < length; i++)
                k[i] = (byte) (i * seed + 7);
            return k;
        }

        /// <summary>Password bytes that are deliberately not valid UTF-8 text.</summary>
        private static byte[] BinaryPassword() => new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x80, 0x7F, 0x2A };

        // ---------------------------------------------------------------- byte[] password

        [TestMethod]
        public void BytePassword_RoundTrips() {
            using var f = new TempFile();
            var pw = BinaryPassword();

            var w = JsonSettings.Configure<Bag>(f).WithEncryption(pw).LoadNow();
            w.Value = "payload";
            w.Number = 42;
            w.Save();

            var r = JsonSettings.Configure<Bag>(f).WithEncryption(pw).LoadNow();
            r.Value.Should().Be("payload");
            r.Number.Should().Be(42);
        }

        [TestMethod]
        public void BytePassword_IsDeterministicAcrossInstances() {
            using var f = new TempFile();

            var written = JsonSettings.Configure<Bag>(f).WithEncryption(Key(20, 3)).LoadNow();
            written.Value = "payload";
            written.Save();

            // A fresh byte[] with the same contents must resolve to the same key.
            var read = JsonSettings.Configure<Bag>(f).WithEncryption(Key(20, 3)).LoadNow();
            read.Value.Should().Be("payload");
        }

        [TestMethod]
        public void BytePassword_WrongPassword_IsReported() {
            using var f = new TempFile();
            var w = JsonSettings.Configure<Bag>(f).WithEncryption(Key(16, 3)).LoadNow();
            w.Value = "payload";
            w.Save();

            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryption(Key(16, 9)).LoadNow())
                .Should().Throw<JsonSettingsException>();
        }

        /// <summary>
        ///     A byte[] password is a different credential from the text password whose UTF-8 encoding
        ///     equals those bytes - the whole reason it is documented as such. A file written with one
        ///     must not read with the other.
        /// </summary>
        [TestMethod]
        public void BytePassword_IsNotInterchangeableWithTheSameBytesAsAString() {
            const string text = "hunter2";
            var bytes = Encoding.UTF8.GetBytes(text);

            using var f = new TempFile();
            var w = JsonSettings.Configure<Bag>(f).WithEncryption(bytes).LoadNow();
            w.Value = "payload";
            w.Save();

            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryption(text).LoadNow())
                .Should().Throw<JsonSettingsException>("a binary password and the text password with the same bytes are distinct credentials");
        }

        [TestMethod]
        public void BytePassword_ViaFetcher_RoundTrips() {
            using var f = new TempFile();
            Func<byte[]> pw = () => Key(24, 5);

            var w = JsonSettings.Configure<Bag>(f).WithEncryption(pw).LoadNow();
            w.Value = "payload";
            w.Save();

            var r = JsonSettings.Configure<Bag>(f).WithEncryption(pw).LoadNow();
            r.Value.Should().Be("payload");
        }

        [TestMethod]
        public void BytePassword_Null_Throws() {
            using var f = new TempFile();
            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryption((byte[]) null))
                .Should().Throw<ArgumentNullException>();
        }

        // ---------------------------------------------------------------- raw key

        [DataTestMethod]
        [DataRow(16, DisplayName = "AES-128")]
        [DataRow(24, DisplayName = "AES-192")]
        [DataRow(32, DisplayName = "AES-256")]
        public void RawKey_RoundTrips(int keyLength) {
            using var f = new TempFile();
            var key = Key(keyLength, 2);

            var w = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(key).LoadNow();
            w.Value = "payload";
            w.Number = 7;
            w.Save();

            var r = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(key).LoadNow();
            r.Value.Should().Be("payload");
            r.Number.Should().Be(7);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(15)]
        [DataRow(17)]
        [DataRow(20)]
        [DataRow(31)]
        [DataRow(33)]
        [DataRow(64)]
        public void RawKey_WrongLength_ThrowsEagerly(int keyLength) {
            using var f = new TempFile();
            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(new byte[keyLength]))
                .Should().Throw<ArgumentException>("a raw AES key must be 16, 24 or 32 bytes")
                .WithMessage("*16, 24 or 32*");
        }

        [TestMethod]
        public void RawKey_WrongKey_IsReported() {
            using var f = new TempFile();
            var w = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(Key(32, 2)).LoadNow();
            w.Value = "payload";
            w.Save();

            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(Key(32, 4)).LoadNow())
                .Should().Throw<JsonSettingsException>();
        }

        [TestMethod]
        public void RawKey_ViaFetcher_RoundTrips() {
            using var f = new TempFile();
            Func<byte[]> key = () => Key(32, 6);

            var w = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(key).LoadNow();
            w.Value = "payload";
            w.Save();

            var r = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(key).LoadNow();
            r.Value.Should().Be("payload");
        }

        /// <summary>
        ///     A fetcher can only be checked when it runs, so an invalid length from one surfaces at
        ///     load time rather than at configuration time.
        /// </summary>
        [TestMethod]
        public void RawKey_FetcherReturningWrongLength_ThrowsOnLoad() {
            using var f = new TempFile();
            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(() => new byte[20]).LoadNow())
                .Should().Throw<ArgumentException>().WithMessage("*16, 24 or 32*");
        }

        [TestMethod]
        public void RawKey_Null_Throws() {
            using var f = new TempFile();
            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryptionRawKey((byte[]) null))
                .Should().Throw<ArgumentNullException>();
        }

        /// <summary>
        ///     The module copies the key at construction, so a caller clearing or reusing its buffer
        ///     afterwards cannot change what the module encrypts with.
        /// </summary>
        [TestMethod]
        public void RawKey_IsDefensivelyCopied() {
            using var f = new TempFile();
            var key = Key(32, 2);
            var original = (byte[]) key.Clone();

            var settings = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(key);
            Array.Clear(key, 0, key.Length); // caller wipes its buffer after handing it over

            settings.Value = "payload";
            settings.Save();

            // Reading with the ORIGINAL bytes must still work: the module kept its own copy.
            var r = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(original).LoadNow();
            r.Value.Should().Be("payload");
        }

        // ---------------------------------------------------------------- separation & interop

        /// <summary>
        ///     The same bytes mean different things as a password (derived) and as a raw key (verbatim),
        ///     so a file written one way must not read the other.
        /// </summary>
        [TestMethod]
        public void RawKey_AndBytePassword_AreDifferentCredentials() {
            var material = Key(32, 2);

            using var f = new TempFile();
            var w = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(material).LoadNow();
            w.Value = "payload";
            w.Save();

            new Action(() => JsonSettings.Configure<Bag>(f).WithEncryption(material).LoadNow())
                .Should().Throw<JsonSettingsException>("a raw key is used verbatim; the same bytes as a password are PBKDF2-derived");
        }

        [TestMethod]
        public void RawKey_ComposesWithBase64() {
            using var f = new TempFile();
            var key = Key(32, 2);

            var w = JsonSettings.Configure<Bag>(f).WithBase64().WithEncryptionRawKey(key).LoadNow();
            w.Value = "payload";
            w.Save();

            var r = JsonSettings.Configure<Bag>(f).WithBase64().WithEncryptionRawKey(key).LoadNow();
            r.Value.Should().Be("payload");
        }

        [TestMethod]
        public void RawKey_ProducesAStandardIvPlusBlocksLayout() {
            using var f = new TempFile();
            var w = JsonSettings.Configure<Bag>(f).WithEncryptionRawKey(Key(32, 2)).LoadNow();
            w.Value = "payload";
            w.Save();

            var bytes = File.ReadAllBytes(f.FileName);
            bytes.Length.Should().BeGreaterThan(16, "the file carries a full IV plus at least one cipher block");
            (bytes.Length % 16).Should().Be(0, "IV plus CBC blocks is always a multiple of the 128-bit block size");
        }

        /// <summary>
        ///     The raw-key path can be built directly on the module too, not only through the fluent API.
        /// </summary>
        [TestMethod]
        public void RawKey_ViaModuleFactory_RoundTrips() {
            using var f = new TempFile();
            var key = Key(32, 8);

            var w = JsonSettings.Configure<Bag>(f).WithModule(EncryptionModule.FromRawKey(key)).LoadNow();
            w.Value = "payload";
            w.Save();

            var r = JsonSettings.Configure<Bag>(f).WithModule(EncryptionModule.FromRawKey(key)).LoadNow();
            r.Value.Should().Be("payload");
        }

        public class Bag : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public int Number { get; set; }
        }
    }
}
