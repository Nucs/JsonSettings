using System;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation.Recovery;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests.Upgrade {
    /// <summary>
    ///     Covers what happens to a damaged encrypted settings file: whether
    ///     <see cref="RecoveryModule"/> gets a chance at it, and what a consumer's
    ///     <c>catch (JsonSettingsException)</c> actually catches.
    /// </summary>
    /// <remarks>
    ///     WHAT THESE ASSERT. As with the rest of this folder: the behaviour of 2.0.1 and 2.0.2,
    ///     measured against the packages rather than inferred. See <see cref="ModuleChainingTests"/>
    ///     for the full note.
    ///
    ///     THE MECHANISM. JsonSettings.Load runs OnDecrypt BEFORE it consults the recovery hook -
    ///     the OnTryingRecover calls sit further down, guarded by an empty-content check and by a
    ///     catch around LoadJson. Anything thrown from inside the decrypt stage therefore bypasses
    ///     recovery entirely, and is only caught by Load's own two filters
    ///     (InvalidOperationException "Cannot convert" and ArgumentException "Invalid").
    ///
    ///     In 2.0.x a short file produced a short read that was silently tolerated, so decryption
    ///     returned empty or garbage bytes and control reached the recovery hook. 2.1.0 rejects the
    ///     short read with an EndOfStreamException, which is correct about the file but changes both
    ///     who handles it and what type escapes.
    ///
    ///     WHY THE ZERO-LENGTH CASE IS THE ONE THAT MATTERS. A settings file of zero bytes is the
    ///     ordinary result of a process dying mid-save, a full disk, or a host that truncates on
    ///     open. It is the single most likely damaged state a real deployment encounters, and it is
    ///     precisely what RecoveryModule exists to absorb.
    /// </remarks>
    [TestClass]
    public class DamagedEncryptedFileTests {
        private const string Password = "SuperPassword";

        /// <summary>
        ///     An intact 80-byte encrypted settings file written by a pre-2.1.0 build, holding
        ///     Value="round-trip me" and Number=42. Shared with
        ///     <see cref="EncryptionCompatibilityTests"/>; truncations of it are the damaged inputs
        ///     below, so every damaged case is a prefix of a file that is known to be otherwise valid.
        /// </summary>
        private const string IntactCiphertextBase64 =
            "Ic7gehKbtG8To0EDPbc+BN0NwL1zxPZ6X9UTc8GO7j0WPEXrk7BJ2cnl35FtOT15LWnBucRwi3iGrUQWnDNfea0mGLXq" +
            "7t0v6V9DsPNCf3o=";

        private static byte[] Intact() => Convert.FromBase64String(IntactCiphertextBase64);

        private static byte[] Truncated(int length) => Intact().Take(length).ToArray();

        // ---------------------------------------------------------------- (b) recovery

        /// <summary>
        ///     A zero-length encrypted file is recoverable.
        /// </summary>
        [TestMethod]
        public void Recovery_AbsorbsAZeroLengthEncryptedFile() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, new byte[0]);

            var s = JsonSettings.Configure<UpgradeSettings>(f)
                                .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                .WithEncryption(Password)
                                .LoadNow();

            s.Value.Should().BeNull("an interrupted save is exactly what RecoveryModule is for");
        }

        [TestMethod]
        public void Recovery_AbsorbsASingleByteEncryptedFile() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, new byte[] { 0x41 });

            var s = JsonSettings.Configure<UpgradeSettings>(f)
                                .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                .WithEncryption(Password)
                                .LoadNow();

            s.Value.Should().BeNull();
        }

        /// <summary>
        ///     Cut inside the 16-byte initialization vector.
        /// </summary>
        [TestMethod]
        public void Recovery_AbsorbsAFileTruncatedInsideTheInitializationVector() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Truncated(10));

            var s = JsonSettings.Configure<UpgradeSettings>(f)
                                .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                .WithEncryption(Password)
                                .LoadNow();

            s.Value.Should().BeNull();
        }

        /// <summary>
        ///     Cut at exactly the IV boundary: a complete IV and no ciphertext at all.
        /// </summary>
        /// <remarks>
        ///     The boundary case, and the reason the length check has to be "fewer than 16" rather
        ///     than "not the whole file". This one behaves identically on 2.0.x and 2.1.0 - the IV
        ///     read is satisfied, decryption yields nothing, and the empty-content branch hands it to
        ///     recovery. It brackets the regression from above.
        /// </remarks>
        [TestMethod]
        public void Recovery_AbsorbsAFileTruncatedExactlyAtTheInitializationVector() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Truncated(16));

            var s = JsonSettings.Configure<UpgradeSettings>(f)
                                .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                .WithEncryption(Password)
                                .LoadNow();

            s.Value.Should().BeNull();
        }

        /// <summary>
        ///     Cut past the IV. Unchanged across versions: padding validation rejects it and the
        ///     wrapped CryptographicException is what surfaces, recovery or not.
        /// </summary>
        [DataTestMethod]
        [DataRow(20)]
        [DataRow(48)]
        public void Recovery_DoesNotAbsorbAFileTruncatedPastTheInitializationVector(int length) {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Truncated(length));

            new Action(() => JsonSettings.Configure<UpgradeSettings>(f)
                                         .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                         .WithEncryption(Password)
                                         .LoadNow())
                .Should().Throw<JsonSettingsException>("a decryptable-looking file that fails padding is reported, not recovered")
                .WithInnerException<System.Security.Cryptography.CryptographicException>();
        }

        /// <summary>
        ///     Random bytes longer than an IV. Unchanged across versions; the control for the
        ///     truncation cases.
        /// </summary>
        [TestMethod]
        public void Recovery_DoesNotAbsorbGarbageLongerThanAnInitializationVector() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Enumerable.Range(0, 64).Select(i => (byte) (i * 7)).ToArray());

            new Action(() => JsonSettings.Configure<UpgradeSettings>(f)
                                         .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                         .WithEncryption(Password)
                                         .LoadNow())
                .Should().Throw<JsonSettingsException>()
                .WithInnerException<System.Security.Cryptography.CryptographicException>();
        }

        /// <summary>
        ///     Plaintext controls. Unaffected by the decrypt-stage change, and green on both versions.
        /// </summary>
        [DataTestMethod]
        [DataRow("{ this is not json", DisplayName = "corrupt")]
        [DataRow("", DisplayName = "empty")]
        [DataRow("   \r\n  ", DisplayName = "whitespace")]
        public void Recovery_AbsorbsDamagedPlaintextFiles(string content) {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Encoding.UTF8.GetBytes(content));

            var s = JsonSettings.Configure<UpgradeSettings>(f)
                                .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                .LoadNow();

            s.Value.Should().BeNull();
        }

        /// <summary>
        ///     With <see cref="RecoveryAction.Throw"/>, the documented exception is
        ///     <see cref="JsonSettingsRecoveryException"/>.
        /// </summary>
        /// <remarks>
        ///     Separate from the catchability tests below because this is the module's own published
        ///     contract rather than a general expectation about base types: RecoveryAction.Throw
        ///     promises that specific type, and a caller distinguishing "recovery declined" from
        ///     other failures has nothing else to switch on.
        /// </remarks>
        [TestMethod]
        public void RecoveryActionThrow_ReportsAJsonSettingsRecoveryException() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Truncated(10));

            new Action(() => JsonSettings.Configure<UpgradeSettings>(f)
                                         .WithRecovery(RecoveryAction.Throw)
                                         .WithEncryption(Password)
                                         .LoadNow())
                .Should().Throw<JsonSettingsRecoveryException>();
        }

        // ---------------------------------------------------------------- (c) catchability

        /// <summary>
        ///     Every load failure this library produces is a <see cref="JsonSettingsException"/>.
        /// </summary>
        /// <remarks>
        ///     This is the contract the README's examples lean on and the reason JsonSettingsException
        ///     exists: one type to catch around a load, whatever the file turned out to contain.
        ///     EndOfStreamException derives from IOException, not from JsonSettingsException, so a
        ///     consumer catching the documented type no longer covers a short file.
        ///
        ///     The rows are ordered from "least damaged" to "most", and the two that regressed are
        ///     both cases where the file is shorter than an initialization vector.
        /// </remarks>
        /// <remarks>
        ///     Without a <see cref="RecoveryModule"/> every row here throws on 2.0.x - the point is
        ///     not whether it throws but WHICH type comes out. Measured on 2.0.1:
        ///
        ///         len 0, 1, 10, 16  ->  JsonSettingsException "The settings file is empty!"
        ///         len 20, 48        ->  JsonSettingsException "Password appears to be invalid."
        ///
        ///     Every one of them is catchable as JsonSettingsException, which is the contract.
        /// </remarks>
        [DataTestMethod]
        [DataRow(0, DisplayName = "zero-length encrypted file")]
        [DataRow(1, DisplayName = "one byte")]
        [DataRow(10, DisplayName = "truncated inside the IV")]
        [DataRow(16, DisplayName = "IV only, no ciphertext")]
        [DataRow(20, DisplayName = "truncated past the IV")]
        [DataRow(48, DisplayName = "truncated on a block boundary")]
        public void EveryEncryptedLoadFailure_IsAJsonSettingsException(int length) {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Truncated(length));

            new Action(() => JsonSettings.Configure<UpgradeSettings>(f)
                                         .WithEncryption(Password)
                                         .LoadNow())
                .Should().Throw<JsonSettingsException>("a consumer catching the library's own exception type must not have another type escape past it");
        }

        /// <summary>
        ///     A wrong password is a <see cref="JsonSettingsException"/>. Unchanged, and the control:
        ///     it proves the rows above are about file length rather than about encryption failing in
        ///     general.
        /// </summary>
        [DataTestMethod]
        [DataRow("definitely-wrong", DisplayName = "ordinary wrong password")]
        [DataRow("p433", DisplayName = "wrong password whose output survives padding validation")]
        public void AWrongPassword_IsAJsonSettingsException(string password) {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Intact());

            new Action(() => JsonSettings.Configure<UpgradeSettings>(f).WithEncryption(password).LoadNow())
                .Should().Throw<JsonSettingsException>();
        }

        /// <summary>
        ///     The intact file with the right password. If this ever goes red, nothing else in this
        ///     class means anything.
        /// </summary>
        [TestMethod]
        public void TheIntactFixture_LoadsWithTheCorrectPassword() {
            using var f = new TempFile();
            File.WriteAllBytes(f.FileName, Intact());

            var s = JsonSettings.Configure<CompatShape>(f).WithEncryption(Password).LoadNow();

            s.Value.Should().Be("round-trip me");
            s.Number.Should().Be(42);
        }

        /// <summary>
        ///     Matches the shape the fixture was serialised from.
        /// </summary>
        public class CompatShape : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public int Number { get; set; }
        }
    }
}
