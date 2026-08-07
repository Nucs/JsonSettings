using System;
using System.IO;
using System.Security;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Covers the <see cref="FluentJsonSettings"/> extension overloads the existing fluent tests do
    ///     not touch: the <see cref="FileInfo"/> filename overload, <c>WithModule&lt;T,TMod&gt;</c>,
    ///     <c>WithDefaultValues</c>, the instance-fetcher encryption overloads, the algorithm-selecting
    ///     binary-password overload, the raw-key instance fetcher, <c>LoadNow(filename)</c> and the
    ///     string-versioned <c>WithVersioning</c>.
    /// </summary>
    [TestClass]
    public class FluentApiCoverageTests {
        private static readonly byte[] PasswordBytes = Encoding.UTF8.GetBytes("binary-password");

        private static byte[] Key32() {
            var k = new byte[32];
            for (int i = 0; i < k.Length; i++) k[i] = (byte) (i * 7 + 1);
            return k;
        }

        [TestMethod]
        public void WithFileName_FileInfoOverload_ResolvesPath() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<FluentSettings>().WithFileName(new FileInfo(f.FileName)).LoadNow();
            o.FileName.Should().EndWith(Path.GetFileName(f.FileName));
        }

        [TestMethod]
        public void WithModuleGeneric_ConstructsAndAttachesModule() {
            using var f = new TempFile();
            var o = new SettingsBag().WithFileName((string) f).WithModule<SettingsBag, Base64Module>().LoadNow();
            o.Modulation.IsAttachedOfType<Base64Module>().Should().BeTrue();

            o["k"] = "v";
            o.Save();
            //Base64 makes the file text-safe base64 rather than raw JSON.
            var raw = File.ReadAllText(o.FileName);
            raw.Should().NotContain("\"k\"");
        }

        [TestMethod]
        public void WithDefaultValues_RunsTheAction() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<FluentSettings>(f)
                                .WithDefaultValues(s => s.Value = "preset")
                                .LoadNow();
            o.Value.Should().Be("preset");
        }

        [TestMethod]
        public void WithDefaultValues_NullAction_Throws() {
            new Action(() => JsonSettings.Configure<FluentSettings>().WithDefaultValues(null!))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void WithEncryption_InstanceSecureStringFetcher_RoundTrips() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryption((Func<FluentSettings, SecureString>) (s => "pw".ToSecureString()))
                                .LoadNow();
            o.Value = "secret";
            o.Save();

            var x = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryption((Func<FluentSettings, SecureString>) (s => "pw".ToSecureString()))
                                .LoadNow();
            x.Value.Should().Be("secret");
        }

        [TestMethod]
        public void WithEncryption_BinaryPasswordWithAlgorithm_RoundTrips() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryption(PasswordBytes, EncryptionAlgorithm.AesCbcHmac)
                                .LoadNow();
            o.Value = "authenticated";
            o.Save();

            var x = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryption(PasswordBytes, EncryptionAlgorithm.AesCbcHmac)
                                .LoadNow();
            x.Value.Should().Be("authenticated");
        }

        [TestMethod]
        public void WithEncryption_InstanceBinaryFetcher_RoundTrips() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryption((Func<FluentSettings, byte[]>) (s => PasswordBytes))
                                .LoadNow();
            o.Value = "bytes";
            o.Save();

            var x = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryption((Func<FluentSettings, byte[]>) (s => PasswordBytes))
                                .LoadNow();
            x.Value.Should().Be("bytes");
        }

        [TestMethod]
        public void WithEncryptionRawKey_InstanceFetcher_RoundTrips() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryptionRawKey((Func<FluentSettings, byte[]>) (s => Key32()))
                                .LoadNow();
            o.Value = "rawkey";
            o.Save();

            var x = JsonSettings.Configure<FluentSettings>(f)
                                .WithEncryptionRawKey((Func<FluentSettings, byte[]>) (s => Key32()))
                                .LoadNow();
            x.Value.Should().Be("rawkey");
        }

        [TestMethod]
        public void LoadNow_WithExplicitFileName_LoadsFromThatFile() {
            using var f = new TempFile();
            //Configure without a filename, then hand LoadNow the specific path.
            var o = JsonSettings.Configure<FluentSettings>().LoadNow(f.FileName);
            o.Value = "explicit";
            o.Save();

            var x = JsonSettings.Configure<FluentSettings>().LoadNow(f.FileName);
            x.Value.Should().Be("explicit");
        }

        [TestMethod]
        public void WithVersioning_StringExpectedVersion_Parses() {
            using var f = new TempFile();
            var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning("1.0.0.0", VersioningResultAction.DoNothing)
                                  .LoadNow();
            cfg.Version.Should().Be(new Version(1, 0, 0, 0));
        }

        public class FluentSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public FluentSettings() { }
            public FluentSettings(string fileName) : base(fileName) { }
        }
    }
}
