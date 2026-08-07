using System;
using System.Security;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

//RijndaelModule is intentionally [Obsolete]; this file exists to keep the backward-compatibility shim
//tested. Suppress the obsolete-usage warning for the whole file rather than at every call site.
#pragma warning disable 618

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Exercises every constructor and factory of the obsolete <see cref="RijndaelModule"/> shim.
    ///     The shim forwards to <see cref="EncryptionModule"/> (default AES-256-CBC), so each construction
    ///     must still produce a working, on-disk-compatible round-trip. Only the <c>string</c> constructor
    ///     and <c>FromRawKey(byte[])</c> were previously covered.
    /// </summary>
    [TestClass]
    public class ObsoleteRijndaelModuleTests {
        private static readonly byte[] PasswordBytes = Encoding.UTF8.GetBytes("rij-binary");

        private static byte[] Key32() {
            var k = new byte[32];
            for (int i = 0; i < k.Length; i++) k[i] = (byte) (i + 3);
            return k;
        }

        private static void RoundTrip(Func<RijndaelModule> moduleFactory) {
            using var f = new TempFile();
            var o = JsonSettings.Configure<RijSettings>(f).WithModule(moduleFactory()).LoadNow();
            o.Value = "kept";
            o.Save();

            var x = JsonSettings.Configure<RijSettings>(f).WithModule(moduleFactory()).LoadNow();
            x.Value.Should().Be("kept");
        }

        [TestMethod]
        public void Constructor_SecureString_RoundTrips() {
            RoundTrip(() => new RijndaelModule("pw".ToSecureString()));
        }

        [TestMethod]
        public void Constructor_FuncString_RoundTrips() {
            RoundTrip(() => new RijndaelModule((Func<string>) (() => "pw")));
        }

        [TestMethod]
        public void Constructor_FuncSecureString_RoundTrips() {
            RoundTrip(() => new RijndaelModule((Func<SecureString>) (() => "pw".ToSecureString())));
        }

        [TestMethod]
        public void Constructor_ByteArray_RoundTrips() {
            RoundTrip(() => new RijndaelModule(PasswordBytes));
        }

        [TestMethod]
        public void Constructor_FuncByteArray_RoundTrips() {
            RoundTrip(() => new RijndaelModule((Func<byte[]>) (() => PasswordBytes)));
        }

        [TestMethod]
        public void FromRawKey_Fetcher_RoundTrips() {
            RoundTrip(() => RijndaelModule.FromRawKey((Func<byte[]>) Key32));
        }

        [TestMethod]
        public void FromRawKey_Fetcher_Null_Throws() {
            new Action(() => RijndaelModule.FromRawKey((Func<byte[]>) null!))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void FromRawKey_ByteArray_Null_Throws() {
            new Action(() => RijndaelModule.FromRawKey((byte[]) null!))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void RijndaelModule_WritesWhatEncryptionModuleReads() {
            //The shim and the modern module share an on-disk format for the same text password.
            using var f = new TempFile();
            var written = JsonSettings.Configure<RijSettings>(f).WithModule(new RijndaelModule("compat")).LoadNow();
            written.Value = "shared";
            written.Save();

            var read = JsonSettings.Configure<RijSettings>(f).WithModule(new EncryptionModule("compat")).LoadNow();
            read.Value.Should().Be("shared");
        }

        public class RijSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public RijSettings() { }
            public RijSettings(string fileName) : base(fileName) { }
        }
    }
}
