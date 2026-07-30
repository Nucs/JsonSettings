using System;
using System.Security;
using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Modulation {
    /// <summary>
    ///     Unit tests for <see cref="EncryptionModule"/> plumbing that the file round-trip tests do not
    ///     reach directly: the <see cref="EncryptionModule.Password"/> setter, the
    ///     <see cref="Func{TResult}"/> password constructors (including the "make the fetched
    ///     SecureString read-only" step), <see cref="EncryptionModule.Deattach"/>, and a raw-key
    ///     AES-CBC-HMAC round-trip driven through the module (the raw-key path the fluent AES-length
    ///     validation would otherwise refuse a 48-byte key on).
    /// </summary>
    [TestClass]
    public class EncryptionInternalsTests {
        [TestMethod]
        public void PasswordSetter_RoundTripsValue() {
            var m = new EncryptionModule("initial");
            m.Password = "changed".ToSecureString();
            m.Password.ToRawString().Should().Be("changed");
        }

        [TestMethod]
        public void FuncStringConstructor_ResolvesPassword() {
            var m = new EncryptionModule(() => "resolved");
            m.Password.ToRawString().Should().Be("resolved");
        }

        [TestMethod]
        public void FuncSecureStringConstructor_MakesFetchedSecureStringReadOnly() {
            //A fetcher may hand back a still-mutable SecureString; the module hardens it to read-only
            //before use.
            var m = new EncryptionModule(() => {
                var s = new SecureString();
                s.AppendChar('a');
                s.AppendChar('b');
                return s; //deliberately NOT read-only
            });

            var resolved = m.Password;
            resolved.IsReadOnly().Should().BeTrue();
            resolved.ToRawString().Should().Be("ab");
        }

        [TestMethod]
        public void NullPasswordFetcherResult_FallsBackToEmpty() {
            var m = new EncryptionModule((Func<SecureString>) (() => null!));
            m.Password.Should().BeSameAs(EncryptionModule.EmptyString);
        }

        [TestMethod]
        public void Deattach_RemovesModuleAndCryptoHandlers() {
            var s = new EncSettings();
            var m = new EncryptionModule("p");
            s.Modulation.Attach(m);
            s.Modulation.IsAttachedOfType<EncryptionModule>().Should().BeTrue();

            s.Modulation.Deattach(m);
            s.Modulation.IsAttachedOfType<EncryptionModule>().Should().BeFalse();
        }

        [TestMethod]
        public void RawKeyAesCbcHmac_ViaModule_RoundTrips() {
            using var f = new TempFile();
            var key = new byte[48]; //16-byte AES key + 32-byte HMAC key
            RandomNumberGenerator.Create().GetBytes(key);

            EncryptionModule Build() {
                var module = EncryptionModule.FromRawKey(() => key);
                module.Algorithm = EncryptionAlgorithm.AesCbcHmac;
                return module;
            }

            var o = JsonSettings.Configure<EncSettings>(f).WithModule(Build()).LoadNow();
            o.Value = "authenticated";
            o.Save();

            var x = JsonSettings.Configure<EncSettings>(f).WithModule(Build()).LoadNow();
            x.Value.Should().Be("authenticated");
        }

        [TestMethod]
        public void RawKeyAesCbcHmac_WrongKey_IsReported() {
            using var f = new TempFile();
            var key = new byte[48];
            RandomNumberGenerator.Create().GetBytes(key);

            EncryptionModule Build(byte[] k) {
                var module = EncryptionModule.FromRawKey(() => k);
                module.Algorithm = EncryptionAlgorithm.AesCbcHmac;
                return module;
            }

            var o = JsonSettings.Configure<EncSettings>(f).WithModule(Build(key)).LoadNow();
            o.Value = "secret";
            o.Save();

            var wrong = new byte[48];
            RandomNumberGenerator.Create().GetBytes(wrong);
            //A wrong HMAC key fails authentication -> surfaced as the library's own exception.
            new Action(() => JsonSettings.Configure<EncSettings>(f).WithModule(Build(wrong)).LoadNow())
                .Should().Throw<JsonSettingsException>();
        }

        public class EncSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public EncSettings() { }
            public EncSettings(string fileName) : base(fileName) { }
        }
    }
}
