using System;
using System.IO;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings.Tests.Utils;

namespace Nucs.JsonSettings.Tests {
    /// <summary>
    ///     Unhappy-flow coverage for the core <see cref="JsonSettings"/> load/save path: the two write
    ///     failures <see cref="JsonSettings.Save(JsonSettings,string)"/> is documented to translate into a
    ///     catchable <see cref="JsonSettingsException"/> (a read-only file and a file another handle holds
    ///     exclusively), the two-argument <c>Load(filename, configure)</c> null guard, a corrupt on-disk
    ///     file surfacing as the library's own exception rather than a raw Json.NET one, and the
    ///     configure-state guard rolling back so a settings instance whose <c>OnConfigure</c> threw can be
    ///     loaded again.
    /// </summary>
    [TestClass]
    public class CoreLoadSaveEdgeTests {
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // ---- Save failures are wrapped as JsonSettingsException --------------------------------

        [TestMethod]
        public void Save_ToReadOnlyFile_IsWrappedAsJsonSettingsException() {
            //The file-open guard raises UnauthorizedAccessException for a read-only target; Save must
            //present that as the library's own exception, not leak the framework type. Windows-guarded
            //for the same reason the sibling FilesTests guard file-sharing behaviour: a CI container
            //running as root can write a read-only file and would not throw at all.
            if (!IsWindows)
                Assert.Inconclusive("Read-only enforcement on write is only a reliable guarantee on Windows here.");

            using var f = new TempFile(create: true);
            var s = JsonSettings.Load<PlainCoreSettings>(f.FileName);
            s.Value = "before";

            File.SetAttributes(f.FileName, FileAttributes.ReadOnly);
            try {
                new Action(() => s.Save())
                    .Should().Throw<JsonSettingsException>().WithMessage("*Failed writing*");
            } finally {
                File.SetAttributes(f.FileName, FileAttributes.Normal);
            }
        }

        [TestMethod]
        public void Save_WhileFileHeldExclusively_IsWrappedAsJsonSettingsException() {
            //Save opens the file with FileShare.None and now asks the open helper to surface a sharing
            //violation (rather than returning a null stream and NRE-ing on SetLength). A second handle
            //holding the file exclusively must therefore produce a catchable JsonSettingsException.
            if (!IsWindows)
                Assert.Inconclusive("Deterministic FileShare.None locking across handles is a Windows guarantee.");

            using var f = new TempFile(create: true);
            var s = JsonSettings.Load<PlainCoreSettings>(f.FileName);
            s.Value = "held";

            using (File.Open(f.FileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                new Action(() => s.Save())
                    .Should().Throw<JsonSettingsException>().WithMessage("*Failed writing*");
            }
        }

        // ---- Load guards / corrupt input ------------------------------------------------------

        [TestMethod]
        public void Load_FilenameAndConfigureOverload_NullFileName_Throws() {
            //The (filename, configure) overload has its own null guard, distinct from the single-argument
            //Load(string) one.
            var s = JsonSettings.Construct<PlainCoreSettings>();
            new Action(() => s.Load((string) null!, _ => { }))
                .Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        public void Load_CorruptFile_ThrowsCatchableJsonSettingsException() {
            //A hand-edited file whose value shape no longer matches the property type (here an object
            //where a string is expected) must fail as the one exception a caller is told to catch around
            //a load -- not a raw Newtonsoft JsonReaderException/JsonSerializationException.
            using var f = new TempFile(create: true);
            File.WriteAllText(f.FileName, "{\"Value\": { \"unexpected\": \"object\" } }");

            new Action(() => JsonSettings.Load<PlainCoreSettings>(f.FileName))
                .Should().Throw<JsonSettingsException>();
        }

        // ---- Configure-state rollback ---------------------------------------------------------

        [TestMethod]
        public void OnConfigure_ThatThrewOnce_IsRetriedOnTheSameInstance() {
            //EnsureConfigured rolls the guard back to NotConfigured when OnConfigure throws, so a later
            //call retries rather than wedging the instance in a half-configured state forever. Built with
            //`new` (not Construct, which configures eagerly) so the first configuration happens on Load.
            using var f = new TempFile();
            var s = new RetryConfigureSettings { FileName = f.FileName };

            s.ThrowOnNextConfigure = true;
            new Action(() => s.Load(f.FileName))
                .Should().Throw<InvalidOperationException>().WithMessage("*configure boom*");

            s.ThrowOnNextConfigure = false;
            new Action(() => s.Load(f.FileName))
                .Should().NotThrow("the rolled-back guard lets configuration be attempted again");
            s.ConfigureAttempts.Should().Be(2, "OnConfigure ran once per Load, the first throwing and the second succeeding");
        }

        // ---- helpers --------------------------------------------------------------------------

        public class PlainCoreSettings : JsonSettings {
            public override string FileName { get; set; }
            public string Value { get; set; }
            public PlainCoreSettings() { }
            public PlainCoreSettings(string fileName) : base(fileName) { }
        }

        public class RetryConfigureSettings : JsonSettings {
            public override string FileName { get; set; }
            public bool ThrowOnNextConfigure;
            public int ConfigureAttempts;

            protected override void OnConfigure() {
                base.OnConfigure();
                ConfigureAttempts++;
                if (ThrowOnNextConfigure)
                    throw new InvalidOperationException("configure boom");
            }

            public RetryConfigureSettings() { }
            public RetryConfigureSettings(string fileName) : base(fileName) { }
        }
    }
}
