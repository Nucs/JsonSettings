using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nucs.JsonSettings;
using Nucs.JsonSettings.Fluent;
using Nucs.JsonSettings.Modulation;
using Nucs.JsonSettings.Modulation.Recovery;


namespace Nucs.JsonSettings.Tests {
    [TestClass]
    public class RecoveryModuleTests {
        TempfileLife FindFile(string baseFile, Version version) {
            baseFile = Path.GetFullPath(baseFile);
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(baseFile), Path.HasExtension(baseFile) ? "*" + Path.GetFileNameWithoutExtension(baseFile) + "*" : "*.*", SearchOption.TopDirectoryOnly)) {
                if (Path.GetFileName(file).Contains("." + version))
                    return new TempfileLife(file);
            }

            throw new FileNotFoundException(version.ToString());
        }

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
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

        [TestMethod]
        public void RenameAndLoadDefault_Case2() {
            using (var f = new TempfileLife(false)) {
                var Settings = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f)
                                       .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                       .LoadNow();
            
                Settings.Type = "Hello"; //Boom! saves.
                //someone changed the file manually and messed up the json.
                File.WriteAllText(f, File.ReadAllText(f, Encoding.UTF8).Replace("Hello", "Hi\"}\n:={}"), Encoding.UTF8); //some random text breaking the json
            
                //after some changes and development, you decide to upgrade to 1.0.0.7
                Settings = JsonSettings.Configure<RecoveryWithoutVersionSettings>(f)
                                       .WithRecovery(RecoveryAction.RenameAndLoadDefault)
                                       .LoadNow();

                Console.WriteLine(f.FileName);

                File.Exists(f.FileName.Replace(".json", ".0.json")).Should().BeTrue();
                Settings.Type.Should().Be("Hi");
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


    public class RecoveryWithoutVersionSettings : JsonSettings {
        #region Overrides of JsonSettings

        public override string FileName { get; set; }

        #endregion

        public virtual string Type { get; set; } = "Hi";
        
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