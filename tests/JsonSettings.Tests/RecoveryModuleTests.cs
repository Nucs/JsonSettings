using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;
using Nucs.JsonSettings.xTests.Utils;
using NUnit.Framework;

namespace Nucs.JsonSettings.xTests {
    [TestFixture]
    public class RecoveryModuleTests {
        TempfileLife FindFile(string baseFile, Version version) {
            baseFile = Path.GetFullPath(baseFile);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(baseFile), Path.HasExtension(baseFile) ? "*" + Path.GetFileNameWithoutExtension(baseFile) + "*" : "*.*", SearchOption.TopDirectoryOnly)) {
                if (Path.GetFileName(file).Contains("." + version))
                    return new TempfileLife(file);
            }

            throw new FileNotFoundException(version.ToString());
        }

        [TestCase]
        public void LoadDefault_ReloadOnCorruption() {
            using (var f = new TempfileLife(false)) {
                //load
                var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                      .WithRecovery(RecoveryAction.LoadDefault)
                                      .LoadNow();

                cfg.Value = 5;
                cfg.Save();


                //load
                var cfgg = JsonSettings.Configure<ThrowingRecoverySettings>(f)
                                       .WithRecovery(RecoveryAction.LoadDefault)
                                       .LoadNow();

                cfgg.Value.Should().BeNullOrEmpty();
            }
        }

        [TestCase]
        public void Throw_Case1() {
            using (var f = new TempfileLife(false)) {
                //assert 
                var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                      .WithVersioning(new Version(1, 0, 0, 2), VersioningResultAction.DoNothing)
                                      .LoadNow();

                cfg.Version.Should().Be(new Version(1, 0, 0, 2));

                new Action(() => JsonSettings.Configure<ThrowingRecoverySettings>(f)
                                             .WithRecovery(RecoveryAction.Throw)
                                             .WithVersioning(new Version(1, 0, 0, 2), VersioningResultAction.RenameAndLoadDefault)
                                             .LoadNow()).ShouldThrow<JsonSettingsRecoveryException>();
            }
        }

        [TestCase]
        public void LoadDefaultAndSave_Case1() {
            using (var f = new TempfileLife(false)) {
                //load
                var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                      .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();

                //assert
                cfg.Version.Should().Be(new Version(1, 0, 0, 0));

                //load
                cfg = JsonSettings.Configure<RecoverySettings>(f)
                                  .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.LoadDefaultAndSave)
                                  .LoadNow();
                cfg.Version.Should().Be(new Version(1, 2, 0, 0));

                new Action(() => {
                    cfg = JsonSettings.Configure<RecoverySettings>(f)
                                      .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();
                }).ShouldNotThrow();
            }
        }

        [TestCase]
        public void RenameAndLoadDefault_Case1() {
            using (var f = new TempfileLife(false)) {
                //load
                var cfg = JsonSettings.Configure<RecoverySettings>(f)
                                      .WithRecovery(RecoveryAction.Throw)
                                      .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();
                cfg.Value = 1;
                cfg.Save();
                cfg.Version.Should().Be(new Version(1, 0, 0, 0));
                //assert

                //load
                var cfg2 = JsonSettings.Configure<ThrowingRecoverySettings>(f)
                                       .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                       .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                                       .LoadNow();

                cfg2.Version.Should().Be(new Version(1, 2, 0, 0));
                using var _1_0_0_0 = FindFile(f, new Version(1, 0, 0, 0));
                /*
                new Action(() => {
                    cfg = JsonSettings.Configure<RecoverySettings>(f)
                                      .WithVersioning(new Version(1, 1, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();
                }).ShouldThrow<InvalidVersionException>();*/
            }
        }
    }

    public class RecoverySettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual int Value { get; set; }
    }

    public class ThrowingRecoverySettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual int[] Value { get; set; }
    }
}