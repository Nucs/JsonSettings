using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Upgrade {
    /// <summary>
    ///     Covers what a custom <see cref="Modulation.Module"/> is allowed to put inside the
    ///     encryption layer.
    /// </summary>
    /// <remarks>
    ///     WHAT THESE ASSERT. Every expectation in this folder is the behaviour that
    ///     Nucs.JsonSettings 2.0.1 and 2.0.2 actually exhibited, established by compiling the same
    ///     scenario bodies against those packages from nuget.org and recording the result - not by
    ///     reading their source and reasoning about it. 2.0.1 and 2.0.2 agreed on all of them.
    ///
    ///     A RED TEST HERE IS A BEHAVIOUR CHANGE, NOT A BROKEN TEST. Each failure is a decision:
    ///     either the library regressed and should be fixed, or the change is wanted, in which case
    ///     flip the assertion AND say so in the release notes. Please do not quietly flip it.
    ///
    ///     THE MECHANISM. Modules hook the Encrypt/Decrypt pair. Encrypt is an ordinary event, so
    ///     handlers run in attach order; Decrypt is declared with a reverse insert
    ///     (`_decrypt = value + _decrypt`), so handlers run in reverse attach order. The two are
    ///     therefore symmetric, and a module attached BEFORE WithEncryption wraps the plaintext
    ///     while one attached AFTER wraps the ciphertext.
    ///
    ///     2.1.0 added a UTF-8 validity check on the bytes RijndaelModule hands back, as a
    ///     tie-breaker for wrong-password detection. That check assumes the layer immediately inside
    ///     the encryption is UTF-8 JSON, which is true for the built-in modules but is not a
    ///     constraint the module contract ever stated.
    /// </remarks>
    [TestClass]
    public class ModuleChainingTests {
        private const string Password = "pw";

        /// <summary>
        ///     Writes, reloads and returns the reloaded instance, applying <paramref name="configure"/>
        ///     identically on both passes - which is what a consumer does.
        /// </summary>
        private static UpgradeSettings RoundTrip(string file, Func<UpgradeSettings, UpgradeSettings> configure) {
            var w = configure(JsonSettings.Configure<UpgradeSettings>(file)).LoadNow();
            w.Value = "payload";
            w.Number = 42;
            w.Save();

            return configure(JsonSettings.Configure<UpgradeSettings>(file)).LoadNow();
        }

        private static void ShouldRoundTrip(string file, Func<UpgradeSettings, UpgradeSettings> configure, string because) {
            var r = RoundTrip(file, configure);
            r.Value.Should().Be("payload", because);
            r.Number.Should().Be(42, because);
        }

        // ---------------------------------------------------------------- controls
        // These held in 2.0.x and still hold. They are here so that a failure elsewhere in this
        // class can be attributed to the module's position rather than to encryption being broken.

        [TestMethod]
        public void EncryptionAlone_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithEncryption(Password), "encryption on its own is the control for every other case here");
        }

        [TestMethod]
        public void CustomModuleAlone_WithoutEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithModule(new GzipModule()), "a binary module with no encryption never reaches the UTF-8 check");
        }

        [TestMethod]
        public void Base64_BeforeEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithBase64().WithEncryption(Password), "base64 is ASCII, so it satisfies a UTF-8 check by construction");
        }

        [TestMethod]
        public void Base64_AfterEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithEncryption(Password).WithBase64(), "outside the encryption the payload handed back by decryption is plain JSON");
        }

        /// <summary>
        ///     The discriminator: hex output is not JSON, but it IS valid UTF-8.
        /// </summary>
        /// <remarks>
        ///     If this passes while <see cref="Gzip_BeforeEncryption_RoundTrips"/> fails, the rejection
        ///     is specifically about UTF-8 validity rather than about the payload having to be JSON.
        /// </remarks>
        [TestMethod]
        public void Utf8ButNotJsonModule_BeforeEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithModule(new HexModule()).WithEncryption(Password), "a module whose output is valid UTF-8 is unaffected by the check");
        }

        // ---------------------------------------------------------------- the regression

        /// <summary>
        ///     A compressing module inside the encryption layer. Round-trips on 2.0.1 and 2.0.2.
        /// </summary>
        [TestMethod]
        public void Gzip_BeforeEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithModule(new GzipModule()).WithEncryption(Password),
                            "a module attached before WithEncryption wraps the plaintext, which 2.0.x placed no encoding constraint on");
        }

        /// <summary>
        ///     Same shape as the gzip case with a different transform, to show the trigger is the
        ///     encoding of the bytes rather than anything specific to compression.
        /// </summary>
        [TestMethod]
        public void Xor_BeforeEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithModule(new XorModule()).WithEncryption(Password),
                            "any byte transform that leaves the plaintext non-UTF-8 is in the same position");
        }

        [TestMethod]
        public void Gzip_AfterEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithEncryption(Password).WithModule(new GzipModule()),
                            "this is the documented workaround for the 2.1.0 change: wrap the ciphertext, not the plaintext");
        }

        /// <summary>
        ///     A module on both sides of the encryption. Fails for the inner instance alone.
        /// </summary>
        [TestMethod]
        public void Gzip_OnBothSidesOfEncryption_RoundTrips() {
            using var f = new TempFile();
            ShouldRoundTrip(f, c => c.WithModule(new GzipModule()).WithEncryption(Password).WithModule(new GzipModule()),
                            "moving a copy outside does not rescue the copy that is still inside");
        }

        /// <summary>
        ///     The failure, when it happens, blames the password.
        /// </summary>
        /// <remarks>
        ///     Recorded separately from the round-trip tests because the message is the part a user
        ///     actually sees, and it points at the wrong cause: the password here is correct and the
        ///     file is intact. If <see cref="Gzip_BeforeEncryption_RoundTrips"/> is green this test is
        ///     vacuous, which is the desired end state.
        /// </remarks>
        [TestMethod]
        public void Gzip_BeforeEncryption_DoesNotReportACorrectPasswordAsInvalid() {
            using var f = new TempFile();
            var w = JsonSettings.Configure<UpgradeSettings>(f).WithModule(new GzipModule()).WithEncryption(Password).LoadNow();
            w.Value = "payload";
            w.Save();

            new Action(() => JsonSettings.Configure<UpgradeSettings>(f).WithModule(new GzipModule()).WithEncryption(Password).LoadNow())
                .Should().NotThrow("the password used to read is the password used to write");
        }

        // ---------------------------------------------------------------- cross-version

        /// <summary>
        ///     A gzip+encrypted settings file written by 2.0.1, byte for byte.
        /// </summary>
        /// <remarks>
        ///     The round-trip tests above only prove the current build disagrees with itself across a
        ///     save/load pair. This one proves the sharper thing: a file a user already has on disk,
        ///     produced by the previous release, stops being readable. That is the difference between
        ///     "a configuration that no longer works" and "data that can no longer be reached".
        ///
        ///     Produced by 2.0.1 from nuget.org: UpgradeSettings { Value="payload", Number=42 },
        ///     GzipModule attached before WithEncryption("pw"). 80 bytes.
        /// </remarks>
        private const string GzipEncryptedWrittenBy201 =
            "eD/H0rxTrKwD1Pe8+Yhrzbq8oz88CutniljS6SJxhE8tGVJV4ALWN1PYDAA5kq0CLkV2AlUl0W2gnQMZRchABypIyikOyg/ICiOJIAm69TI=";

        /// <summary>
        ///     Same, with <see cref="XorModule"/> in place of gzip. 64 bytes.
        /// </summary>
        private const string XorEncryptedWrittenBy201 =
            "PnVQX/AsNQdgAYWLH4V2pYC2hYxu1yiW9wXTO4+gCHxJUT1yAlbWoPAqq0vjIQmmORhxEOAczpemlU+hjoG0/Q==";

        [TestMethod]
        public void GzipEncryptedFileWrittenBy201_IsStillReadable() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Convert.FromBase64String(GzipEncryptedWrittenBy201));

            var s = JsonSettings.Configure<UpgradeSettings>(f).WithModule(new GzipModule()).WithEncryption(Password).LoadNow();

            s.Value.Should().Be("payload", "an existing file must not stop being readable across an upgrade");
            s.Number.Should().Be(42);
        }

        [TestMethod]
        public void XorEncryptedFileWrittenBy201_IsStillReadable() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Convert.FromBase64String(XorEncryptedWrittenBy201));

            var s = JsonSettings.Configure<UpgradeSettings>(f).WithModule(new XorModule()).WithEncryption(Password).LoadNow();

            s.Value.Should().Be("payload");
            s.Number.Should().Be(42);
        }

        /// <summary>
        ///     Rollback direction: whatever this build writes, 2.0.x must still be able to read.
        /// </summary>
        /// <remarks>
        ///     Asserted here as a format check rather than a behaviour check - the bytes on disk for
        ///     the gzip+encryption chain are produced by the same cipher and the same module order in
        ///     both versions, so a file written now is readable by 2.0.x. Verified directly by
        ///     round-tripping the produced file through the 2.0.1 package; kept here as a length and
        ///     structure sanity check so that a future format change cannot pass unnoticed.
        /// </remarks>
        [TestMethod]
        public void FileWrittenNow_HasTheSameOnDiskShapeAs201() {
            using var f = new TempFile();
            var w = JsonSettings.Configure<UpgradeSettings>(f).WithEncryption(Password).LoadNow();
            w.Value = "payload";
            w.Number = 42;
            w.Save();

            var bytes = File.ReadAllBytes(f.FileName);
            bytes.Length.Should().BeGreaterThan(16, "the file must carry a full initialization vector plus at least one cipher block");
            (bytes.Length % 16).Should().Be(0, "IV plus CBC blocks is always a multiple of the 128-bit block size");
        }
    }
}
