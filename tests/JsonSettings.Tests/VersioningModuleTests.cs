using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.xTests.Utils;
using NUnit.Framework;

namespace Nucs.JsonSettings.xTests {
    [TestFixture]
    public class VersioningModuleTests {
        TempfileLife FindFile(string baseFile, Version version) {
            baseFile = Path.GetFullPath(baseFile);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(baseFile), Path.HasExtension(baseFile) ? "*" + Path.GetFileNameWithoutExtension(baseFile) + "*" : "*.*", SearchOption.TopDirectoryOnly)) {
                if (Path.GetFileName(file).Contains("." + version))
                    return new TempfileLife(file);
            }

            throw new FileNotFoundException(version.ToString());
        }

        [TestCase]
        public void RenameAndLoadDefault_Case1() {
            using (var f = new TempfileLife(false)) {
                //load
                var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                      .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                                      .LoadNow();

                //assert
                cfg.Version.Should().Be(new Version(1, 0, 0, 0));

                //load
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.RenameAndLoadDefault)
                                  .LoadNow();

                using var _1_0_0_0 = FindFile(f, new Version(1, 0, 0, 0));

                //change version and save
                cfg.Version = new Version("1.0.0.1");
                cfg.Save();

                //assert 
                cfg.Version.Should().Be(new Version(1, 0, 0, 1));
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 1), VersioningResultAction.RenameAndLoadDefault)
                                  .LoadNow();
                cfg.Version.Should().Be(new Version(1, 0, 0, 1));

                //assert 
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 0, 0, 2), VersioningResultAction.RenameAndLoadDefault)
                                  .LoadNow();
                using var _1_0_0_1 = FindFile(f, new Version(1, 0, 0, 1));

                cfg.Version.Should().Be(new Version(1, 0, 0, 2));
            }
        }

        [TestCase]
        public void Throw_Case1() {
            using (var f = new TempfileLife(false)) {
                //load
                var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                      .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();

                //assert
                cfg.Version.Should().Be(new Version(1, 0, 0, 0));

                //load
                new Action(() => {
                    cfg = JsonSettings.Configure<VersionedSettings>(f)
                                      .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();
                }).ShouldThrow<InvalidVersionException>();
            }
        }

        [TestCase]
        public void LoadDefaultAndSave_Case1() {
            using (var f = new TempfileLife(false)) {
                //load
                var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                      .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();

                //assert
                cfg.Version.Should().Be(new Version(1, 0, 0, 0));

                //load
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.LoadDefaultAndSave)
                                  .LoadNow();
                cfg.Version.Should().Be(new Version(1, 2, 0, 0));

                new Action(() => {
                    cfg = JsonSettings.Configure<VersionedSettings>(f)
                                      .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();
                }).ShouldNotThrow();
            }
        }

        [TestCase]
        public void LoadDefault_Case1() {
            using (var f = new TempfileLife(false)) {
                //load
                var cfg = JsonSettings.Configure<VersionedSettings>(f)
                                      .WithVersioning(new Version(1, 0, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();

                //assert
                cfg.Version.Should().Be(new Version(1, 0, 0, 0));

                //load
                cfg = JsonSettings.Configure<VersionedSettings>(f)
                                  .WithVersioning(new Version(1, 2, 0, 0), VersioningResultAction.LoadDefault)
                                  .LoadNow();

                cfg.Version.Should().Be(new Version(1, 2, 0, 0));

                new Action(() => {
                    cfg = JsonSettings.Configure<VersionedSettings>(f)
                                      .WithVersioning(new Version(1, 1, 0, 0), VersioningResultAction.Throw)
                                      .LoadNow();
                }).ShouldThrow<InvalidVersionException>();
            }
        }
    }

    public class VersionedSettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual int Value { get; set; }
    }
    
    public class ChangedVersionedSettings : JsonSettings, IVersionable {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public Version Version { get; set; } = new Version(1, 0, 0, 0);

        public virtual string Value { get; set; }
    }
}