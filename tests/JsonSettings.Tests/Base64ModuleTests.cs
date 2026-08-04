using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation.Recovery;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Covers a damaged <see cref="Nucs.JsonSettings.Modulation.Base64Module"/> file: a file whose
    ///     bytes are not valid base64 (a truncated or hand-edited save). This is the base64 analogue of
    ///     <see cref="Upgrade.DamagedEncryptedFileTests"/>.
    /// </summary>
    /// <remarks>
    ///     The decode ran in the OnDecrypt stage, ahead of Load's recovery hook and outside its two
    ///     exception filters, so <c>Convert.FromBase64String</c> threw a raw <see cref="FormatException"/>
    ///     that (a) was not a <see cref="JsonSettingsException"/> and (b) bypassed <see cref="RecoveryModule"/>
    ///     entirely. Base64Module now treats an undecodable payload as empty - the text-safe analogue of
    ///     EncryptionModule's short-ciphertext handling - so the empty-content branch routes it to recovery
    ///     (or reports "the settings file is empty!" as a catchable JsonSettingsException).
    /// </remarks>
    [TestClass]
    public class Base64ModuleTests {
        private const string NotBase64 = "this is definitely not valid base64 @@@@ %%%%";

        [TestMethod]
        public void CorruptBase64_WithoutRecovery_ThrowsJsonSettingsException() {
            using var f = new TempFile();
            File.WriteAllText(f.FileName, NotBase64);

            new Action(() => JsonSettings.Configure<Base64Settings>(f).WithBase64().LoadNow())
                .Should().Throw<JsonSettingsException>("a corrupt base64 file must surface as the library's own exception type, not a raw FormatException");
        }

        [TestMethod]
        public void CorruptBase64_WithRecovery_LoadsDefault() {
            using var f = new TempFile();
            File.WriteAllText(f.FileName, NotBase64);

            var s = JsonSettings.Configure<Base64Settings>(f)
                                .WithRecovery(RecoveryAction.LoadDefault)
                                .WithBase64()
                                .LoadNow();

            s.Value.Should().BeNull("an undecodable base64 file is a damaged file RecoveryModule should absorb");
        }

        [TestMethod]
        public void CorruptBase64_WithEncryptionAndRecovery_LoadsDefault() {
            using var f = new TempFile();
            File.WriteAllText(f.FileName, NotBase64);

            //The common combo: base64 wraps the ciphertext for text-safe storage. On load base64 decodes
            //first, so its failure is what must route to recovery.
            var s = JsonSettings.Configure<Base64Settings>(f)
                                .WithRecovery(RecoveryAction.LoadDefault)
                                .WithEncryption("pw")
                                .WithBase64()
                                .LoadNow();

            s.Value.Should().BeNull();
        }

        [TestMethod]
        public void ValidBase64_RoundTrips() {
            using var f = new TempFile();
            var o = JsonSettings.Configure<Base64Settings>(f).WithBase64().LoadNow();
            o.Value = "kept";
            o.Save();

            var x = JsonSettings.Configure<Base64Settings>(f).WithBase64().LoadNow();
            x.Value.Should().Be("kept", "the fix must not disturb an ordinary valid base64 round-trip");
        }

        public class Base64Settings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public Base64Settings() { }
            public Base64Settings(string fileName) : base(fileName) { }
        }
    }
}
